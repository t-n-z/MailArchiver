namespace EmailArchiveViewer;

/// <summary>One row in the email list grid. Carries the .msg path so the detail view can open it.</summary>
public sealed class MessageRow
{
    public string Sender { get; init; } = "";
    public string Receiver { get; init; } = "";
    public DateTime? Date { get; init; }
    public string Subject { get; init; } = "";
    public string FilePath { get; init; } = "";

    /// <summary>Sortable, fixed-width date text for the grid.</summary>
    public string DateText => Date?.ToString("yyyy-MM-dd HH:mm") ?? "";
}

/// <summary>Fully-parsed email for the detail view.</summary>
public sealed class EmailDetail
{
    public string Sender { get; init; } = "";
    public string To { get; init; } = "";
    public string Cc { get; init; } = "";
    public DateTime? Date { get; init; }
    public string Subject { get; init; } = "";
    public string Body { get; init; } = "";
    public List<AttachmentRef> Attachments { get; init; } = [];
}

/// <summary>An attachment's name and raw bytes — extracted to temp + opened on click.</summary>
public sealed class AttachmentRef
{
    public string FileName { get; init; } = "attachment";
    public byte[] Data { get; init; } = [];
}
