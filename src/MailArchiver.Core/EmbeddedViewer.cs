using System.Reflection;

namespace MailArchiver;

/// <summary>
/// Carries the WinForms viewer (EmailArchiveViewer.exe) as an embedded resource and drops
/// it into a backup's root folder, so every backup is self-browsing. Best-effort — any
/// failure here logs a warning and never affects the backup itself.
/// </summary>
public static class EmbeddedViewer
{
    private const string ResourceName = "EmailArchiveViewer.exe";
    private const string FileName = "EmailArchiveViewer.exe";

    /// <summary>Writes the viewer into <paramref name="outDir"/> if missing or a different size.</summary>
    public static void Extract(string outDir)
    {
        try
        {
            // The viewer is embedded in the program exe (Outlook or Mime), not in Core,
            // so read from the entry assembly.
            Assembly? asm = Assembly.GetEntryAssembly();
            using Stream? res = asm?.GetManifestResourceStream(ResourceName);
            if (res is null)
                return; // not embedded (e.g. a quick dev build without the viewer) — skip silently

            string target = Path.Combine(outDir, FileName);
            if (File.Exists(target) && new FileInfo(target).Length == res.Length)
                return; // already up to date

            using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            res.CopyTo(fs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not place the viewer in the backup folder: {ex.Message}");
        }
    }
}
