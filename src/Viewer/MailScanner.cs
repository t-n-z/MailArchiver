namespace EmailArchiveViewer;

/// <summary>A backup folder that holds mail — one entry in the folder dropdown.</summary>
public sealed class MailFolder
{
    public string DisplayPath { get; init; } = "";
    public string FullPath { get; init; } = "";
    public override string ToString() => DisplayPath;
}

/// <summary>
/// Walks the backup tree (rooted at the viewer exe's own directory) and finds folders that
/// contain mail. Handles both backup formats:
///  - `.msg` (Outlook) — a folder counts as mail only if peeking its first few files yields
///    an Email-type item, so Contacts/Calendar/Tasks folders are left out;
///  - `.eml` (MIME sources) — every folder with `.eml` files is mail (those formats carry
///    no contact/calendar items).
/// </summary>
public static class MailScanner
{
    private const int PeekCount = 5;

    public static List<MailFolder> Scan(string root)
    {
        var result = new List<MailFolder>();
        try
        {
            ScanDir(root, root, result);
        }
        catch { /* unreadable tree — return whatever we found */ }

        result.Sort((a, b) => string.Compare(a.DisplayPath, b.DisplayPath, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static void ScanDir(string root, string dir, List<MailFolder> result)
    {
        string[] msgs = SafeGetFiles(dir, "*.msg");
        string[] emls = SafeGetFiles(dir, "*.eml");

        bool isMail = emls.Length > 0
                      || (msgs.Length > 0 && msgs.Take(PeekCount).Any(MsgFacade.IsMailMsg));
        if (isMail)
        {
            string rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
            result.Add(new MailFolder
            {
                DisplayPath = rel == "." ? "(root)" : rel,
                FullPath = dir,
            });
        }

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(dir); }
        catch { return; }

        foreach (string sub in subdirs)
            ScanDir(root, sub, result);
    }

    /// <summary>Parses every .msg and .eml in a folder into list rows, newest first.</summary>
    public static List<MessageRow> LoadFolder(string folderPath)
    {
        string[] files = [.. SafeGetFiles(folderPath, "*.msg"), .. SafeGetFiles(folderPath, "*.eml")];

        var rows = new List<MessageRow>(files.Length);
        foreach (string f in files)
            rows.Add(MailFacade.ReadRow(f));

        rows.Sort((a, b) => Nullable.Compare(b.Date, a.Date)); // newest first
        return rows;
    }

    private static string[] SafeGetFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }
}
