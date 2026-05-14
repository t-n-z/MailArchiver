namespace MailArchiver;

/// <summary>
/// A mailbox to archive — an Outlook `.ost`/`.pst`, an mbox/Thunderbird store, a Maildir, an
/// EML directory, etc. Each format supplies its own implementation; the Worker drives only
/// this abstraction so the rest of the program is format-agnostic.
/// </summary>
public interface IMailSource : IDisposable
{
    /// <summary>The top of the mailbox folder tree.</summary>
    IMailFolder Root { get; }

    /// <summary>Native export extension for messages from this source — ".msg" or ".eml".</summary>
    string FileExtension { get; }
}

/// <summary>
/// A nameless container folder — the root of a multi-account source, holding one child
/// folder per account and no messages of its own. The Worker does not materialise a
/// directory for a folder whose name is empty, so accounts land directly under --out.
/// </summary>
public sealed class ContainerFolder : IMailFolder
{
    public string Name { get; init; } = "";
    public List<IMailFolder> Children { get; } = [];
    public int MessageCount => 0;
    public IEnumerable<IMailFolder> SubFolders => Children;
    public IEnumerable<IMailMessage> Messages => [];
}

/// <summary>One folder in a mailbox tree.</summary>
public interface IMailFolder
{
    /// <summary>Display name of this folder (used, sanitized, as the on-disk folder name).</summary>
    string Name { get; }

    /// <summary>Message count for the progress total — cheap for most formats; for mbox this
    /// may be a fast pre-scan.</summary>
    int MessageCount { get; }

    IEnumerable<IMailFolder> SubFolders { get; }
    IEnumerable<IMailMessage> Messages { get; }
}

/// <summary>
/// One message. The field <em>sources</em> differ per format, but the hashing/naming
/// algorithms in Core operate only through this interface.
/// </summary>
public interface IMailMessage
{
    string? Subject { get; }
    string? From { get; }
    string? To { get; }
    string? Cc { get; }
    string? MessageId { get; }
    DateTime? Date { get; }

    /// <summary>
    /// Cheap, header-only identity string for the fast-skip "quick key". Implementations
    /// MUST compose this from fields that do NOT require loading the message body.
    /// </summary>
    string QuickKeyMaterial { get; }

    /// <summary>
    /// Full content-identity string (loads body + an attachment digest). Hashed into the
    /// authoritative content hash used for dedup.
    /// </summary>
    string ContentHashMaterial { get; }

    /// <summary>Writes this message to <paramref name="path"/> in the source's native export
    /// format (`.msg` for Outlook, `.eml` for MIME sources).</summary>
    void SaveTo(string path);

    /// <summary>Releases any memory or handles held for this message.</summary>
    void Release();
}
