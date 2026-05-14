using System.Text;
using MimeKit;

namespace MailArchiver;

/// <summary>
/// <see cref="IMailMessage"/> over a MimeKit <see cref="MimeMessage"/>. Used for every MIME
/// source — mbox, Maildir, EML. Messages are exported as `.eml` via <c>WriteTo</c>.
///
/// The underlying message can be supplied directly (mbox streaming, already parsed) or
/// behind a loader (Maildir/EML — loaded lazily from the file on first access). A loader
/// failure surfaces as an exception from a property, which the Worker records as an error
/// for that one message — the run continues.
/// </summary>
public sealed class MimeMailMessage : IMailMessage
{
    private readonly Func<MimeMessage> _loader;
    private MimeMessage? _msg;
    private MimeMessage Msg => _msg ??= _loader();

    /// <summary>Wraps an already-parsed message (mbox streaming).</summary>
    public MimeMailMessage(MimeMessage msg)
    {
        _msg = msg;
        // An already-parsed message cannot be re-loaded; if Release() has nulled _msg,
        // a later access should fail loudly rather than hand back a disposed message.
        _loader = static () => throw new ObjectDisposedException(nameof(MimeMailMessage),
            "This message was released and cannot be accessed again.");
    }

    /// <summary>Wraps a message to be loaded on demand (Maildir / EML files).</summary>
    public MimeMailMessage(Func<MimeMessage> loader) => _loader = loader;

    public string? Subject => Safe(() => Msg.Subject);
    public string? From => Safe(() => Msg.From?.ToString());
    public string? To => Safe(() => Msg.To?.ToString());
    public string? Cc => Safe(() => Msg.Cc?.ToString());
    public string? MessageId => Safe(() => Msg.MessageId);

    public DateTime? Date
    {
        get
        {
            try
            {
                DateTimeOffset d = Msg.Date;
                return d == DateTimeOffset.MinValue ? null : d.UtcDateTime;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Cheap header-only key. MIME messages almost always carry a stable Message-ID, so this
    /// is a stronger quick key than the Outlook heuristic.
    /// </summary>
    public string QuickKeyMaterial
    {
        get
        {
            var sb = new StringBuilder(256);
            sb.Append("mid:").Append(Safe(() => Msg.MessageId)).Append('\n');
            sb.Append("subj:").Append(Safe(() => Msg.Subject)).Append('\n');
            sb.Append("date:").Append(SafeDate()).Append('\n');
            sb.Append("from:").Append(Safe(() => Msg.From?.ToString())).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>Full content identity — message-id, headers, body text and an attachment digest.</summary>
    public string ContentHashMaterial
    {
        get
        {
            var sb = new StringBuilder(512);
            sb.Append("mid:").Append(Safe(() => Msg.MessageId)).Append('\n');
            sb.Append("subj:").Append(Safe(() => Msg.Subject)).Append('\n');
            sb.Append("date:").Append(SafeDate()).Append('\n');
            sb.Append("from:").Append(Safe(() => Msg.From?.ToString())).Append('\n');
            sb.Append("to:").Append(Safe(() => Msg.To?.ToString())).Append('\n');
            sb.Append("cc:").Append(Safe(() => Msg.Cc?.ToString())).Append('\n');
            sb.Append("body:").Append(Safe(() => Msg.TextBody ?? Msg.HtmlBody)).Append('\n');

            sb.Append("att:");
            try
            {
                var atts = Msg.Attachments
                    .Select(AttachmentDigest)
                    .OrderBy(s => s, StringComparer.Ordinal);
                foreach (string a in atts) sb.Append(a).Append(';');
            }
            catch { /* malformed attachment tree — ignore for the digest */ }
            return sb.ToString();
        }
    }

    public void SaveTo(string path) => Msg.WriteTo(path);

    /// <summary>MimeMessage holds attachment content streams — dispose to free them promptly.</summary>
    public void Release()
    {
        try { _msg?.Dispose(); }
        catch { /* best effort */ }
        _msg = null;
    }

    private static string AttachmentDigest(MimeEntity entity)
    {
        string name = (entity as MimePart)?.FileName
                      ?? entity.ContentType?.Name
                      ?? "attachment";
        long size = 0;
        try
        {
            if (entity is MimePart { Content: { Stream.CanSeek: true } content })
                size = content.Stream.Length;
        }
        catch { /* size is best-effort */ }
        return $"{name}|{size}";
    }

    private static string Safe(Func<string?> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private string SafeDate()
    {
        try
        {
            DateTimeOffset d = Msg.Date;
            return d == DateTimeOffset.MinValue ? string.Empty : d.ToUniversalTime().ToString("o");
        }
        catch { return string.Empty; }
    }
}
