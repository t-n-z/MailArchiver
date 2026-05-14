using System.Diagnostics;

namespace MailArchiver;

/// <summary>
/// Orchestrates the backup by running the actual work in a child <see cref="Worker"/>
/// process. A corrupt block in the data file can hang native decompression while holding
/// XstReader's stream lock — unrecoverable in-process. The supervisor watches the worker's
/// heartbeat; if it stalls, it kills the whole worker process (the OS reclaims the lock
/// and handle), records the offending message as poisoned so it is skipped, and respawns
/// the worker, which resumes from the last checkpoint.
///
/// The supervisor owns the run marker, the progress display, and the data-file copy; the
/// worker owns the index DB. They never touch the DB at the same time — the supervisor
/// only writes poison rows while no worker is running.
/// </summary>
public sealed class Supervisor
{
    private readonly CliArgs _args;
    private readonly bool _redirected = Console.IsOutputRedirected;

    // Shared with the stdout-reader thread — guarded by _lock.
    private readonly object _lock = new();
    private DateTime _lastLineUtc;
    private long _total = -1;
    private bool _sawTotal;
    private long _workerScanned, _written, _skipped, _errored;
    private string _currentFolder = "";
    private bool _hasInflight;
    private long _inflightSeq;
    private string _inflightFolder = "";
    private string _inflightQuick = "";

    // Progress-display state.
    private DateTime _lastProgress = DateTime.MinValue;
    private bool _progressLineOnScreen;

    // The worker child currently running, so shutdown handlers can kill it.
    private volatile Process? _currentWorker;

    private readonly IReadOnlyList<string> _accounts;

    public Supervisor(CliArgs args, IReadOnlyList<string> accounts)
    {
        _args = args;
        _accounts = accounts;
    }

    public int Run()
    {
        string? tempCopy = null;
        RunMarker? marker = null;
        var overall = Stopwatch.StartNew();

        // If we are killed (Ctrl+C, scheduler "End Task", logoff), take the worker child
        // down with us — otherwise it orphans and keeps the data file and DB locked.
        Console.CancelKeyPress += (_, _) => KillCurrentWorker();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillCurrentWorker();

        try
        {
            // No mail accounts in the source at all — nothing to do.
            if (_accounts.Count == 0)
            {
                Console.Error.WriteLine($"No mail accounts found in: {_args.SourcePath}");
                return 2;
            }

            // Multiple accounts — confirm before archiving everything, but only the first
            // time (or when the set of accounts changes). The confirmed set is remembered
            // in the index DB so a routine re-run does not keep asking.
            // A later version will replace this with a per-account selection menu.
            if (_accounts.Count > 1)
            {
                string currentSet = string.Join(
                    "\n", _accounts.OrderBy(a => a, StringComparer.OrdinalIgnoreCase));
                string? confirmedSet = ReadConfirmedAccounts();

                if (!string.Equals(confirmedSet, currentSet, StringComparison.Ordinal))
                {
                    Console.WriteLine($"Found {_accounts.Count} accounts in {_args.SourcePath}:");
                    foreach (string a in _accounts)
                        Console.WriteLine($"  - {a}");
                    if (confirmedSet is not null)
                        Console.WriteLine("  (the set of accounts has changed since last run)");

                    if (Console.IsInputRedirected)
                    {
                        Console.WriteLine($"Proceeding with all {_accounts.Count} accounts (non-interactive).");
                    }
                    else
                    {
                        Console.Write($"Archive all {_accounts.Count} accounts? [y/n]: ");
                        string? reply = Console.ReadLine();
                        if (!string.Equals(reply?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Aborted — nothing was backed up.");
                            return 2;
                        }
                    }

                    // Proceeding — remember this set so we do not ask again until it changes.
                    WriteConfirmedAccounts(currentSet);
                }
            }

            // If a file source is locked by another program (typically Outlook or
            // Thunderbird), offer to back up from a temp copy instead. Directory sources
            // (Maildir / EML tree / Thunderbird profile) skip this upfront check.
            // Non-interactive runs cannot prompt — they get a clear --copy-first message.
            bool copyFirst = _args.CopyFirst;
            if (!copyFirst && File.Exists(_args.SourcePath) && IsFileLocked(_args.SourcePath))
            {
                if (Console.IsInputRedirected)
                {
                    Console.Error.WriteLine(
                        "Data file is in use by another program (e.g. Outlook). " +
                        "Re-run with --copy-first, or close the program holding it.");
                    return 2;
                }
                Console.Write("Data file is in use. Copy to a temp file, back up from the " +
                              "copy, then delete the copy? [y/n]: ");
                string? answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Aborted — nothing was backed up.");
                    return 2;
                }
                copyFirst = true;
            }

            Directory.CreateDirectory(_args.OutDir);
            EmbeddedViewer.Extract(_args.OutDir);
            marker = RunMarker.Acquire(_args.OutDir, _args.SourcePath, out bool wasInterrupted);

            if (wasInterrupted)
            {
                Console.WriteLine("Previous run did not finish cleanly — resuming.");
                int swept = SweepStaleTmp(_args.OutDir);
                if (swept > 0) Console.WriteLine($"  Removed {swept} stale .tmp file(s).");
            }

            string dataPath = _args.SourcePath;
            if (copyFirst)
            {
                tempCopy = Path.Combine(
                    Path.GetTempPath(),
                    $"mailarchiver_{Guid.NewGuid():N}{Path.GetExtension(_args.SourcePath)}");
                Console.WriteLine($"Copying data file to temp: {tempCopy}");
                try
                {
                    File.Copy(_args.SourcePath, tempCopy, overwrite: true);
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Could not copy the data file: {ex.Message}");
                    Console.Error.WriteLine("The program holding it (likely Outlook) denies all " +
                                            "access — even a copy. Close that program and try again.");
                    return 2;
                }
                dataPath = tempCopy;
            }

            Console.WriteLine($"Mirroring \"{_args.Folder ?? "(whole store)"}\" -> {_args.OutDir}");

            int code = SuperviseLoop(dataPath, marker);

            marker.Release();
            PrintSummary(overall.Elapsed);
            return code;
        }
        catch (Exception ex)
        {
            // Leave the marker in place so the next run knows this one was interrupted.
            FinishProgressLine();
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }
        finally
        {
            if (tempCopy is not null) TryDeleteQuiet(tempCopy);
        }
    }

    /// <summary>Spawns workers, killing and respawning any that stall, until the job finishes.</summary>
    private int SuperviseLoop(string dataPath, RunMarker marker)
    {
        bool skipCount = _args.SkipCount;
        int poisonCount = 0;
        int unproductiveRespawns = 0;

        while (true)
        {
            WorkerResult result = RunWorkerOnce(dataPath, skipCount, marker);

            if (!result.Stalled)
            {
                // Worker exited on its own.
                if (result.ExitCode == 2)
                {
                    FinishProgressLine();
                    Console.Error.WriteLine("Worker reported a fatal error (see above).");
                    return 2;
                }
                return result.ExitCode; // 0 ok, 1 ran with per-message errors
            }

            // Worker stalled and was killed. Counting only ever needs to run once.
            if (result.SawTotal) skipCount = true;

            if (result.HadInflight)
            {
                FinishProgressLine();
                using (var index = new ArchiveIndex(_args.ResolvedDbPath))
                    index.MarkPoisoned(result.InflightFolder, result.InflightQuick,
                        $"skipped: stalled >{_args.MsgTimeoutSeconds}s (corrupt block?)");
                poisonCount++;
                unproductiveRespawns = 0; // poisoning is forward progress
                Console.Error.WriteLine(
                    $"  Stalled on a message in \"{result.InflightFolder}\" — poisoned and skipped " +
                    $"(#{poisonCount}). Respawning worker...");

                if (poisonCount > _args.MaxPoison)
                {
                    Console.Error.WriteLine(
                        $"FATAL: exceeded --max-poison ({_args.MaxPoison}). The data file may be " +
                        "badly corrupt. Aborting.");
                    return 2;
                }
            }
            else
            {
                // Stalled before any message was in flight — counting or folder
                // enumeration. Retry without the count pass.
                skipCount = true;
                unproductiveRespawns++;
                FinishProgressLine();
                Console.Error.WriteLine(
                    "  Stalled before processing a message — retrying without the count pass...");

                if (unproductiveRespawns > 5)
                {
                    Console.Error.WriteLine(
                        "FATAL: worker keeps stalling without making progress. Aborting.");
                    return 2;
                }
            }
        }
    }

    private WorkerResult RunWorkerOnce(string dataPath, bool skipCount, RunMarker marker)
    {
        lock (_lock)
        {
            _lastLineUtc = DateTime.UtcNow;
            _workerScanned = _written = _skipped = _errored = 0;
            _hasInflight = false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath
                       ?? throw new InvalidOperationException("Cannot locate own executable path."),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--worker");
        psi.ArgumentList.Add("--source"); psi.ArgumentList.Add(dataPath);
        psi.ArgumentList.Add("--out"); psi.ArgumentList.Add(_args.OutDir);
        psi.ArgumentList.Add("--db"); psi.ArgumentList.Add(_args.ResolvedDbPath);
        psi.ArgumentList.Add("--name-maxlen"); psi.ArgumentList.Add(_args.NameMaxLen.ToString());
        if (_args.Folder is not null) { psi.ArgumentList.Add("--folder"); psi.ArgumentList.Add(_args.Folder); }
        if (_args.Format is not null) { psi.ArgumentList.Add("--format"); psi.ArgumentList.Add(_args.Format); }
        if (skipCount) psi.ArgumentList.Add("--skip-count");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start worker process.");
        _currentWorker = proc;
        try
        {
            var stdoutReader = new Thread(() => ReadStdout(proc)) { IsBackground = true };
            var stderrReader = new Thread(() => RelayStderr(proc)) { IsBackground = true };
            stdoutReader.Start();
            stderrReader.Start();

            bool stalled = false;
            var timeout = TimeSpan.FromSeconds(_args.MsgTimeoutSeconds);
            var etaClock = Stopwatch.StartNew();

            while (!proc.WaitForExit(500))
            {
                DateTime lastLine;
                lock (_lock) lastLine = _lastLineUtc;

                if (DateTime.UtcNow - lastLine > timeout)
                {
                    stalled = true;
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    proc.WaitForExit();
                    break;
                }
                DrawProgress(etaClock);
                marker.Touch();
            }

            // Let the reader threads drain any final buffered output.
            stdoutReader.Join(2000);
            stderrReader.Join(2000);

            lock (_lock)
            {
                return new WorkerResult
                {
                    Stalled = stalled,
                    ExitCode = stalled ? -1 : SafeExitCode(proc),
                    SawTotal = _sawTotal,
                    HadInflight = stalled && _hasInflight,
                    InflightFolder = _inflightFolder,
                    InflightQuick = _inflightQuick,
                };
            }
        }
        finally
        {
            _currentWorker = null;
        }
    }

    /// <summary>Kills the worker child if one is running — invoked from shutdown handlers.</summary>
    private void KillCurrentWorker()
    {
        Process? w = _currentWorker;
        if (w is null) return;
        try
        {
            if (!w.HasExited) w.Kill(entireProcessTree: true);
        }
        catch { /* already gone or disposed — nothing to do */ }
    }

    private static int SafeExitCode(Process proc)
    {
        try { return proc.ExitCode; }
        catch { return 2; }
    }

    private void ReadStdout(Process proc)
    {
        string? line;
        while ((line = proc.StandardOutput.ReadLine()) is not null)
        {
            string[] p = line.Split('\t');
            lock (_lock)
            {
                _lastLineUtc = DateTime.UtcNow;
                switch (p[0])
                {
                    case "TOTAL" when p.Length >= 2 && long.TryParse(p[1], out long t):
                        _total = t;
                        _sawTotal = true;
                        break;
                    case "F" when p.Length >= 2:
                        _currentFolder = p[1];
                        break;
                    case "M" when p.Length >= 4:
                        _hasInflight = true;
                        long.TryParse(p[1], out _inflightSeq);
                        _inflightFolder = p[2];
                        _inflightQuick = p[3];
                        _currentFolder = p[2];
                        break;
                    case "MDONE" when p.Length >= 3:
                        if (long.TryParse(p[1], out long doneSeq) && doneSeq == _inflightSeq)
                            _hasInflight = false;
                        _workerScanned++;
                        switch (p[2])
                        {
                            case "w": _written++; break;
                            case "s" or "p": _skipped++; break;
                            case "e": _errored++; break;
                        }
                        break;
                }
            }
        }
    }

    private void RelayStderr(Process proc)
    {
        string? line;
        while ((line = proc.StandardError.ReadLine()) is not null)
        {
            ClearProgressLine();
            Console.Error.WriteLine(line);
        }
    }

    private void DrawProgress(Stopwatch etaClock)
    {
        if (_args.Quiet) return;

        DateTime now = DateTime.UtcNow;
        double sinceMs = (now - _lastProgress).TotalMilliseconds;
        if (sinceMs < (_redirected ? 30_000 : 500)) return;
        _lastProgress = now;

        long scanned, total, written, skipped, errored;
        string folder;
        lock (_lock)
        {
            scanned = _workerScanned; total = _total; written = _written;
            skipped = _skipped; errored = _errored; folder = _currentFolder;
        }

        string pct = total > 0 ? $"{Math.Min(100, scanned * 100.0 / total),3:0}%" : "  ?%";
        string totalStr = total > 0 ? total.ToString("N0") : "?";
        string eta = "--:--:--";
        if (total > 0 && scanned > 0)
        {
            double perMsg = etaClock.Elapsed.TotalSeconds / scanned;
            eta = TimeSpan.FromSeconds(perMsg * Math.Max(0, total - scanned)).ToString(@"hh\:mm\:ss");
        }

        string line = $"{pct} | {scanned:N0}/{totalStr} | +{written:N0} | skip {skipped:N0} " +
                      $"| err {errored:N0} | ETA {eta} | {Trunc(folder, 36)}";

        if (_redirected)
        {
            Console.WriteLine(line);
        }
        else
        {
            int width = SafeWindowWidth();
            Console.Write("\r" + (line.Length >= width ? line[..(width - 1)] : line.PadRight(width - 1)));
            _progressLineOnScreen = true;
        }
    }

    private void ClearProgressLine()
    {
        if (!_progressLineOnScreen || _redirected) return;
        int width = SafeWindowWidth();
        Console.Write("\r" + new string(' ', width - 1) + "\r");
        _progressLineOnScreen = false;
    }

    private void FinishProgressLine()
    {
        if (_progressLineOnScreen && !_redirected)
        {
            Console.WriteLine();
            _progressLineOnScreen = false;
        }
    }

    private void PrintSummary(TimeSpan elapsed)
    {
        FinishProgressLine();
        (long archived, long poisoned) = (0, 0);
        try
        {
            using var index = new ArchiveIndex(_args.ResolvedDbPath);
            (archived, poisoned) = index.GetStats();
        }
        catch { /* summary is best-effort */ }

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Archived messages (total in index) : {archived:N0}");
        Console.WriteLine($"  Poisoned/skipped (stalled)         : {poisoned:N0}");
        Console.WriteLine($"  Elapsed                            : {elapsed:hh\\:mm\\:ss}");
        if (poisoned > 0)
            Console.WriteLine("  Note: poisoned messages stalled on a likely-corrupt block and were " +
                              "skipped. They are recorded in the index and will not be retried.");
    }

    private static int SweepStaleTmp(string outDir)
    {
        int n = 0;
        try
        {
            foreach (string tmp in Directory.EnumerateFiles(outDir, "*.tmp", SearchOption.AllDirectories))
            {
                try { File.Delete(tmp); n++; }
                catch { /* best effort */ }
            }
        }
        catch { /* nothing to sweep */ }
        return n;
    }

    private const string ConfirmedAccountsKey = "confirmed_accounts";

    /// <summary>
    /// The last account set the user confirmed for this output, from the index DB — null if
    /// the DB does not exist yet or has no record (so the prompt will be shown).
    /// </summary>
    private string? ReadConfirmedAccounts()
    {
        try
        {
            // Don't create the DB just to read — a fresh run / aborted prompt leaves nothing.
            if (!File.Exists(_args.ResolvedDbPath)) return null;
            using var index = new ArchiveIndex(_args.ResolvedDbPath);
            return index.GetSetting(ConfirmedAccountsKey);
        }
        catch
        {
            return null; // unreadable — treat as not-yet-confirmed
        }
    }

    /// <summary>Records the confirmed account set so routine re-runs do not re-prompt.</summary>
    private void WriteConfirmedAccounts(string accountSet)
    {
        try
        {
            using var index = new ArchiveIndex(_args.ResolvedDbPath);
            index.SetSetting(ConfirmedAccountsKey, accountSet);
        }
        catch { /* best effort — worst case it asks again next run */ }
    }

    /// <summary>
    /// True if the data file cannot be opened for shared reading — typically because
    /// Outlook holds it. Uses the same share mode XstReader will, so a "false" here means
    /// the worker can open it too.
    /// </summary>
    private static bool IsFileLocked(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static int SafeWindowWidth()
    {
        try
        {
            int w = Console.WindowWidth;
            return w > 20 ? w : 80;
        }
        catch { return 80; }
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : "..." + s[^(max - 3)..];

    private static void TryDeleteQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private sealed class WorkerResult
    {
        public bool Stalled { get; init; }
        public int ExitCode { get; init; }
        public bool SawTotal { get; init; }
        public bool HadInflight { get; init; }
        public string InflightFolder { get; init; } = "";
        public string InflightQuick { get; init; } = "";
    }
}
