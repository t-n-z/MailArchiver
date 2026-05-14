using System.Text;

namespace MailArchiver;

/// <summary>Builds sanitized, length-bounded file and folder names from mailbox items.</summary>
public static class MessageNamer
{
    private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();
    private const int ComponentCap = 15; // per-component cap for sender/recipient

    /// <summary>
    /// Base filename (no extension): yyyyMMdd_HHmmss_sender_recip_subject, each part
    /// sanitized, sender/recip capped, whole thing truncated to <paramref name="maxLen"/>.
    /// </summary>
    public static string BuildBaseName(IMailMessage message, int maxLen)
    {
        DateTime dt = message.Date ?? DateTime.MinValue;
        string stamp = dt == DateTime.MinValue ? "00000000_000000" : dt.ToString("yyyyMMdd_HHmmss");

        string sender = Cap(Sanitize(SafeGet(() => message.From)), ComponentCap);
        string recip = Cap(Sanitize(SafeGet(() => message.To)), ComponentCap);
        string subject = Sanitize(SafeGet(() => message.Subject));

        var sb = new StringBuilder(stamp);
        if (sender.Length > 0) sb.Append('_').Append(sender);
        if (recip.Length > 0) sb.Append('_').Append(recip);
        if (subject.Length > 0) sb.Append('_').Append(subject);

        string name = sb.ToString();
        if (name.Length > maxLen) name = name[..maxLen];
        return name.TrimEnd('_', ' ', '.');
    }

    /// <summary>
    /// Sanitizes a single mailbox folder name into one path segment. Format-specific folder
    /// renaming (e.g. Outlook's IPM_SUBTREE → "Emails") is done by the source implementation
    /// before the name reaches here.
    /// </summary>
    public static string SanitizeFolderName(string? raw)
    {
        string s = Sanitize(raw);
        return s.Length == 0 ? "_" : s;
    }

    /// <summary>
    /// Replaces characters illegal in Windows filenames with '_', collapses runs of
    /// whitespace/underscores, and trims. Deterministic: the same input always maps to
    /// the same output, so folder/file names stay consistent across runs.
    /// </summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (c < 0x20 || Array.IndexOf(InvalidFileChars, c) >= 0 || c == ' ')
                sb.Append('_');
            else
                sb.Append(c);
        }

        // collapse runs of '_' and strip leading/trailing separators and dots
        var collapsed = new StringBuilder(sb.Length);
        bool prevUnderscore = false;
        foreach (char c in sb.ToString())
        {
            if (c == '_')
            {
                if (!prevUnderscore) collapsed.Append('_');
                prevUnderscore = true;
            }
            else
            {
                collapsed.Append(c);
                prevUnderscore = false;
            }
        }
        return collapsed.ToString().Trim('_', '.', ' ');
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];

    private static string SafeGet(Func<string?> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }
}
