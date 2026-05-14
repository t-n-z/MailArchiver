using System.Text;
using MimeKit;

namespace EmailArchiveViewer;

/// <summary>Thin wrapper over MimeKit — turns a .eml file into list rows / detail.</summary>
public static class EmlFacade
{
    static EmlFacade()
    {
        // MIME headers/bodies also reference legacy code pages — register the provider so
        // MimeKit can decode them.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Lightweight parse for the list grid. Never throws — bad files yield a placeholder row.</summary>
    public static MessageRow ReadRow(string path)
    {
        try
        {
            MimeMessage m = MimeMessage.Load(path);
            return new MessageRow
            {
                Sender = m.From?.ToString() ?? "",
                Receiver = m.To?.ToString() ?? "",
                Date = DateOrNull(m),
                Subject = m.Subject ?? "",
                FilePath = path,
            };
        }
        catch (Exception ex)
        {
            return new MessageRow
            {
                Sender = "(unreadable)",
                Subject = $"{Path.GetFileName(path)} — {ex.Message}",
                FilePath = path,
            };
        }
    }

    /// <summary>Full parse for the detail view.</summary>
    public static EmailDetail ReadFull(string path)
    {
        MimeMessage m = MimeMessage.Load(path);

        var attachments = new List<AttachmentRef>();
        foreach (MimeEntity entity in m.Attachments)
        {
            try
            {
                switch (entity)
                {
                    case MimePart { Content: not null } part:
                        using (var ms = new MemoryStream())
                        {
                            part.Content.DecodeTo(ms);
                            attachments.Add(new AttachmentRef
                            {
                                FileName = MailFacade.FirstNonEmpty(part.FileName, part.ContentType?.Name, "attachment"),
                                Data = ms.ToArray(),
                            });
                        }
                        break;
                    case MessagePart { Message: not null } embedded:
                        using (var ms = new MemoryStream())
                        {
                            embedded.Message.WriteTo(ms);
                            attachments.Add(new AttachmentRef
                            {
                                FileName = MailFacade.FirstNonEmpty(embedded.Message.Subject, "embedded-message") + ".eml",
                                Data = ms.ToArray(),
                            });
                        }
                        break;
                }
            }
            catch { /* skip an attachment we cannot extract */ }
        }

        return new EmailDetail
        {
            Sender = m.From?.ToString() ?? "",
            To = m.To?.ToString() ?? "",
            Cc = m.Cc?.ToString() ?? "",
            Date = DateOrNull(m),
            Subject = m.Subject ?? "",
            Body = BodyText(m),
            Attachments = attachments,
        };
    }

    private static DateTime? DateOrNull(MimeMessage m)
    {
        try { return m.Date == DateTimeOffset.MinValue ? null : m.Date.LocalDateTime; }
        catch { return null; }
    }

    private static string BodyText(MimeMessage m)
    {
        if (!string.IsNullOrWhiteSpace(m.TextBody))
            return m.TextBody.Replace("\r\n", "\n").Replace('\r', '\n');

        if (!string.IsNullOrWhiteSpace(m.HtmlBody))
            return HtmlText.ToText(m.HtmlBody);

        return "(no text body)";
    }

}
