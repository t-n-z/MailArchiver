using System.Text;
using MsgReader.Outlook;

namespace EmailArchiveViewer;

/// <summary>Thin wrapper over MsgReader — turns a .msg file into list rows / detail / a type check.</summary>
public static class MsgFacade
{
    static MsgFacade()
    {
        // .NET Core ships only a handful of encodings by default; .msg files routinely
        // reference legacy code pages (1252, 1257, 1256, …). Without this, MsgReader
        // throws NotSupportedException on roughly a quarter of real-world messages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>True if the .msg is a mail item (Email / EmailSms / read receipts / …).</summary>
    public static bool IsMailMsg(string path)
    {
        try
        {
            using var msg = new Storage.Message(path, FileAccess.Read);
            return msg.Type.ToString().StartsWith("Email", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Lightweight parse for the list grid. Never throws — bad files yield a placeholder row.</summary>
    public static MessageRow ReadRow(string path)
    {
        try
        {
            using var msg = new Storage.Message(path, FileAccess.Read);
            return new MessageRow
            {
                Sender = SenderText(msg),
                Receiver = RecipientText(msg, RecipientType.To),
                Date = (msg.SentOn ?? msg.ReceivedOn)?.LocalDateTime,
                Subject = msg.Subject ?? "",
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
        using var msg = new Storage.Message(path, FileAccess.Read);

        var attachments = new List<AttachmentRef>();
        foreach (object item in msg.Attachments)
        {
            try
            {
                switch (item)
                {
                    case Storage.Attachment a when !a.Hidden:
                        attachments.Add(new AttachmentRef
                        {
                            FileName = string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName,
                            Data = a.Data ?? [],
                        });
                        break;
                    case Storage.Message embedded:
                        using (var ms = new MemoryStream())
                        {
                            embedded.Save(ms);
                            string name = string.IsNullOrWhiteSpace(embedded.Subject)
                                ? "embedded-message" : embedded.Subject;
                            attachments.Add(new AttachmentRef
                            {
                                FileName = name + ".msg",
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
            Sender = SenderText(msg),
            To = RecipientText(msg, RecipientType.To),
            Cc = RecipientText(msg, RecipientType.Cc),
            Date = (msg.SentOn ?? msg.ReceivedOn)?.LocalDateTime,
            Subject = msg.Subject ?? "",
            Body = BodyText(msg),
            Attachments = attachments,
        };
    }

    private static string SenderText(Storage.Message msg)
    {
        Storage.Sender? s = msg.Sender;
        if (s is null) return "";
        string name = MailFacade.FirstNonEmpty(s.DisplayName, s.Email, s.Raw);
        return string.IsNullOrEmpty(s.Email) || s.Email == name
            ? name
            : $"{name} <{s.Email}>";
    }

    private static string RecipientText(Storage.Message msg, RecipientType type)
    {
        IEnumerable<Storage.Recipient> picked = msg.Recipients.Where(r => r.Type == type);
        var parts = picked
            .Select(r => MailFacade.FirstNonEmpty(r.DisplayName, r.Email, r.Raw))
            .Where(s => s.Length > 0)
            .ToList();
        return string.Join("; ", parts);
    }

    private static string BodyText(Storage.Message msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.BodyText))
            return msg.BodyText.Replace("\r\n", "\n").Replace('\r', '\n');

        if (!string.IsNullOrWhiteSpace(msg.BodyHtml))
            return HtmlText.ToText(msg.BodyHtml);

        return "(no text body — this message may be RTF/HTML only)";
    }

}
