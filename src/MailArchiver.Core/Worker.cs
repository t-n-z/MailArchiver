namespace MailArchiver;

/// <summary>
/// The worker does the actual mailbox read and message-file writing. It runs as a child
/// process of <see cref="Supervisor"/> so a hang deep in a format decoder (e.g. native
/// decompression that holds a lock and cannot be interrupted in-process) can be recovered
/// by the supervisor simply killing this process.
///
/// It drives only the <see cref="IMailSource"/> abstraction, so it is format-agnostic.
/// It reports progress to the supervisor as tab-delimited lines on stdout:
///   TOTAL  &lt;n&gt;
///   F      &lt;folderKey&gt;
///   M      &lt;seq&gt; &lt;folderKey&gt; &lt;quickKey&gt;     (about to process; this is the heartbeat)
///   MDONE  &lt;seq&gt; &lt;w|s|p|e&gt;                  (written / skipped / poison-skipped / error)
///   CT     &lt;folderName&gt;                       (counting heartbeat)
/// Human-readable errors go to stderr. Exit code: 0 ok, 1 ran with errors, 2 fatal.
/// </summary>
public sealed class Worker
{
    private const int CheckpointEvery = 250;

    private readonly CliArgs _args;
    private readonly ArchiveIndex _index;
    private readonly IMailSource _source;
    private readonly TextWriter _out = Console.Out;

    private long _seq;
    private int _sinceCheckpoint;
    private int _errors;

    public Worker(CliArgs args, ArchiveIndex index, IMailSource source)
    {
        _args = args;
        _index = index;
        _source = source;
    }

    public int Run()
    {
        try
        {
            IMailFolder start = ResolveStartFolder(_source.Root);

            if (!_args.SkipCount)
                Emit($"TOTAL\t{CountTotal(start)}");

            Directory.CreateDirectory(_args.OutDir);
            _index.BeginBatch();
            if (string.IsNullOrWhiteSpace(start.Name))
            {
                // Nameless multi-account root — its child accounts go straight under --out,
                // no wrapper directory.
                WalkFolder(start, _args.OutDir, "");
            }
            else
            {
                string startDirName = MessageNamer.SanitizeFolderName(start.Name);
                WalkFolder(start, Path.Combine(_args.OutDir, startDirName), startDirName);
            }
            _index.CommitBatch();
        }
        catch (Exception ex)
        {
            try { _index.CommitBatch(); } catch { /* persist what we can */ }
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }

        return _errors > 0 ? 1 : 0;
    }

    /// <summary>
    /// Cheap pre-pass: sums each folder's MessageCount so the supervisor's progress bar has
    /// a real total. Emits a heartbeat per folder.
    /// </summary>
    private long CountTotal(IMailFolder folder)
    {
        Emit($"CT\t{folder.Name}");
        long n = 0;
        try { n = folder.MessageCount; } catch { /* unknown — treat as 0 */ }
        foreach (IMailFolder sub in folder.SubFolders)
            n += CountTotal(sub);
        return n;
    }

    private void WalkFolder(IMailFolder folder, string outDir, string folderKey)
    {
        EnsureDir(outDir);
        Emit($"F\t{folderKey}");

        foreach (IMailMessage message in folder.Messages)
            ProcessMessage(message, outDir, folderKey);

        // Folder-boundary checkpoint: durably commit this folder's work.
        Checkpoint();

        // Sources return child folders in a stable order, so collision suffixing below is
        // stable across runs.
        var usedChildNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IMailFolder sub in folder.SubFolders)
        {
            string childName = MessageNamer.SanitizeFolderName(sub.Name);
            childName = MakeUniqueChildName(childName, usedChildNames);
            string childKey = folderKey.Length == 0 ? childName : $"{folderKey}/{childName}";
            WalkFolder(sub, Path.Combine(outDir, childName), childKey);
        }
    }

    private void ProcessMessage(IMailMessage message, string outDir, string folderKey)
    {
        long seq = ++_seq;
        char outcome = 'e';
        try
        {
            // The quick key is composed from cheap header fields only — it does not load
            // the body. Emit the heartbeat with this key BEFORE any risky read, so if we
            // then hang the supervisor knows exactly which message to poison.
            string quickKey = ContentHasher.ComputeQuickKey(message);
            Emit($"M\t{seq}\t{folderKey}\t{quickKey}");

            if (_index.TryGetByCheapKey(folderKey, quickKey, out string known, out bool poisoned))
            {
                if (poisoned) { outcome = 'p'; return; }
                if (File.Exists(known)) { outcome = 's'; return; }
                // Recorded but the backup file is gone — fall through and re-create it.
            }

            // Risky from here: loads the message body / attachments, which can stall on a
            // corrupt block. If it hangs, the supervisor kills this process.
            string hash = ContentHasher.Compute(message);
            if (_index.TryGetArchived(folderKey, hash, out string existing) && File.Exists(existing))
            {
                outcome = 's';
                return;
            }

            string baseName = MessageNamer.BuildBaseName(message, _args.NameMaxLen);
            if (baseName.Length == 0) baseName = "message";

            string target = ResolveCollision(outDir, baseName, _source.FileExtension);
            string tmp = target + ".tmp";
            try
            {
                message.SaveTo(tmp);
                File.Move(tmp, target);
            }
            catch
            {
                TryDeleteQuiet(tmp);
                throw;
            }

            _index.Record(folderKey, hash, quickKey, target,
                entryId: null,
                internetId: SafeGet(() => message.MessageId),
                subject: SafeGet(() => message.Subject),
                sentUtc: message.Date);

            outcome = 'w';
            if (++_sinceCheckpoint >= CheckpointEvery)
                Checkpoint();
        }
        catch (Exception ex)
        {
            _errors++;
            outcome = 'e';
            string subj = SafeGet(() => message.Subject) ?? "(unknown)";
            Console.Error.WriteLine($"  ERROR  {folderKey}  \"{subj}\": {ex.Message}");
        }
        finally
        {
            try { message.Release(); } catch { /* best effort */ }
            Emit($"MDONE\t{seq}\t{outcome}");
        }
    }

    private void Checkpoint()
    {
        _index.Checkpoint();
        _sinceCheckpoint = 0;
    }

    /// <summary>
    /// Finds a free path for new content. We only get here when the content was NOT found
    /// in the index for this folder, so any file already occupying the name is a different
    /// message — we append " (1)", " (2)", … rather than overwrite.
    /// </summary>
    private static string ResolveCollision(string dir, string baseName, string ext)
    {
        string candidate = Path.Combine(dir, baseName + ext);
        if (!File.Exists(candidate)) return candidate;
        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string MakeUniqueChildName(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name}_{i}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private IMailFolder ResolveStartFolder(IMailFolder root)
    {
        if (string.IsNullOrWhiteSpace(_args.Folder)) return root;

        string[] segments = _args.Folder
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return root;

        if (segments.Length == 1)
        {
            IMailFolder? hit = FindChild(root, segments[0]) ?? FindRecursive(root, segments[0]);
            return hit ?? throw new InvalidOperationException(
                $"Folder \"{_args.Folder}\" not found in the source.");
        }

        IMailFolder current = root;
        foreach (string seg in segments)
        {
            current = FindChild(current, seg) ?? throw new InvalidOperationException(
                $"Folder path \"{_args.Folder}\" not found (stuck at segment \"{seg}\").");
        }
        return current;
    }

    private static IMailFolder? FindChild(IMailFolder parent, string name) =>
        parent.SubFolders.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IMailFolder? FindRecursive(IMailFolder root, string name)
    {
        var queue = new Queue<IMailFolder>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            IMailFolder f = queue.Dequeue();
            foreach (IMailFolder child in f.SubFolders)
            {
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static string? SafeGet(Func<string?> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static void TryDeleteQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    /// <summary>Writes one protocol line to stdout and flushes immediately.</summary>
    private void Emit(string line)
    {
        _out.WriteLine(line);
        _out.Flush();
    }
}
