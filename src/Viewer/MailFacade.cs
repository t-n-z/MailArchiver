namespace EmailArchiveViewer;

/// <summary>
/// Dispatches list/detail reads to the right format facade by file extension — `.msg`
/// (Outlook backups) goes to <see cref="MsgFacade"/>, `.eml` (MIME backups) to
/// <see cref="EmlFacade"/>. One viewer reads both backup formats.
/// </summary>
public static class MailFacade
{
    public static MessageRow ReadRow(string path) =>
        IsEml(path) ? EmlFacade.ReadRow(path) : MsgFacade.ReadRow(path);

    public static EmailDetail ReadFull(string path) =>
        IsEml(path) ? EmlFacade.ReadFull(path) : MsgFacade.ReadFull(path);

    private static bool IsEml(string path) =>
        Path.GetExtension(path).Equals(".eml", StringComparison.OrdinalIgnoreCase);

    /// <summary>First non-blank value, trimmed; empty string if none. Shared by both facades.</summary>
    public static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return "";
    }
}
