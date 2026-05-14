using System.Diagnostics;

namespace MailArchiver;

/// <summary>
/// A small marker file written next to the index DB (<c>&lt;out&gt;\.mailarchiver.run</c>)
/// for the lifetime of a run. It serves two purposes:
///
///  - Concurrency guard: if a marker exists and its PID is still a live MailArchiver
///    process, a second run refuses to start.
///  - Unclean-shutdown detection: if a marker exists but its process is gone, the
///    previous run was interrupted. The caller sweeps stale ".msg.tmp" files and
///    continues — the index DB already records everything that was durably committed.
///
/// Deleted on a clean finish. Left in place on a crash so the next run can detect it.
/// </summary>
public sealed class RunMarker
{
    private const string FileName = ".mailarchiver.run";

    private readonly string _path;
    private readonly string _dataFile;
    private readonly DateTime _startedUtc;

    private RunMarker(string path, string dataFile)
    {
        _path = path;
        _dataFile = dataFile;
        _startedUtc = DateTime.UtcNow;
    }

    public static string PathFor(string outDir) => Path.Combine(outDir, FileName);

    /// <summary>
    /// Acquires the marker for this process. Throws if another live run holds it.
    /// Sets <paramref name="wasInterrupted"/> when a stale marker from a dead run was found.
    /// </summary>
    public static RunMarker Acquire(string outDir, string dataFile, out bool wasInterrupted)
    {
        string path = PathFor(outDir);
        wasInterrupted = false;

        if (File.Exists(path))
        {
            int? pid = TryReadPid(path);
            if (pid is int p && IsLiveArchiverProcess(p))
                throw new InvalidOperationException(
                    $"Another MailArchiver run (PID {p}) is already writing to {outDir}. " +
                    "Wait for it to finish, or choose a different --out directory.");

            // Marker present but owner is gone -> previous run was interrupted.
            wasInterrupted = true;
        }

        var marker = new RunMarker(path, dataFile);
        marker.Write();
        return marker;
    }

    /// <summary>Refreshes the last-checkpoint timestamp. Called at each checkpoint.</summary>
    public void Touch()
    {
        try { Write(); }
        catch { /* a failed touch is not worth aborting the run */ }
    }

    /// <summary>Removes the marker — call only on a clean finish.</summary>
    public void Release()
    {
        try { File.Delete(_path); }
        catch { /* best effort; a leftover marker just triggers a harmless sweep next run */ }
    }

    private void Write()
    {
        File.WriteAllLines(_path,
        [
            $"pid={Environment.ProcessId}",
            $"datafile={_dataFile}",
            $"started_utc={_startedUtc:o}",
            $"checkpoint_utc={DateTime.UtcNow:o}",
        ]);
    }

    private static int? TryReadPid(string path)
    {
        try
        {
            foreach (string line in File.ReadAllLines(path))
                if (line.StartsWith("pid=", StringComparison.Ordinal)
                    && int.TryParse(line.AsSpan(4), out int pid))
                    return pid;
        }
        catch { /* unreadable marker — treat as no pid */ }
        return null;
    }

    private static bool IsLiveArchiverProcess(int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            return !proc.HasExited
                && proc.ProcessName.Contains("MailArchiver", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // No process with that id — it's dead.
            return false;
        }
    }
}
