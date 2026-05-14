using System.Diagnostics;
using System.Text;

namespace EmailArchiveViewer;

/// <summary>
/// Detail pane for a single email: attachment links across the top, then a monospace
/// read-only block with metadata, a separator, the subject, a separator, and the body.
/// </summary>
public sealed class EmailView : UserControl
{
    private readonly FlowLayoutPanel _attachments;
    private readonly TextBox _text;

    public EmailView()
    {
        _attachments = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(6, 4, 6, 4),
            BackColor = SystemColors.ControlLight,
        };
        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
        };

        Controls.Add(_text);        // fill
        Controls.Add(_attachments); // docked above the fill
    }

    public void Show(EmailDetail d)
    {
        _attachments.Controls.Clear();
        if (d.Attachments.Count == 0)
        {
            _attachments.Controls.Add(new Label
            {
                Text = "(no attachments)",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(3),
            });
        }
        else
        {
            foreach (AttachmentRef att in d.Attachments)
            {
                var link = new LinkLabel
                {
                    Text = att.FileName,
                    AutoSize = true,
                    Padding = new Padding(4, 3, 8, 3),
                };
                AttachmentRef captured = att;
                link.LinkClicked += (_, _) => OpenAttachment(captured);
                _attachments.Controls.Add(link);
            }
        }

        var sb = new StringBuilder();
        sb.Append("From:    ").AppendLine(d.Sender);
        sb.Append("To:      ").AppendLine(d.To);
        if (!string.IsNullOrWhiteSpace(d.Cc))
            sb.Append("Cc:      ").AppendLine(d.Cc);
        sb.Append("Date:    ").AppendLine(d.Date?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
        sb.AppendLine(new string('-', 78));
        sb.Append("Subject: ").AppendLine(d.Subject);
        sb.AppendLine(new string('-', 78));
        sb.AppendLine();
        sb.Append(d.Body);

        _text.Text = sb.ToString();
        _text.Select(0, 0);
        _text.ScrollToCaret();
    }

    private static void OpenAttachment(AttachmentRef att)
    {
        try
        {
            string safe = string.Concat(att.FileName.Split(Path.GetInvalidFileNameChars()));
            if (safe.Length == 0) safe = "attachment";

            string dir = Path.Combine(Path.GetTempPath(), "EmailArchiveViewer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, safe);
            File.WriteAllBytes(path, att.Data);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open attachment:\n{ex.Message}", "Attachment",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
