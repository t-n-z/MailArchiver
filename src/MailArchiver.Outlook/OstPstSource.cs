using System.Reflection;
using System.Text;
using XstReader;
using XstReader.Exporter.MsgKit;

namespace MailArchiver;

/// <summary>
/// <see cref="IMailSource"/> over Outlook data files, backed by the vendored (patched)
/// XstReader. Messages are exported as `.msg` via MsgKit.
///
/// A file source is one `.ost`/`.pst`. A directory source is every `.ost`/`.pst` found
/// under it — each becomes one account folder beneath a nameless root.
/// </summary>
public sealed class OstPstSource : IMailSource
{
    private static readonly string[] DataFileExtensions = [".ost", ".pst"];

    private readonly List<XstFile> _files;
    private readonly IMailFolder _root;

    private OstPstSource(List<XstFile> files, IMailFolder root)
    {
        _files = files;
        _root = root;
    }

    public IMailFolder Root => _root;
    public string FileExtension => ".msg";

    public void Dispose()
    {
        foreach (XstFile f in _files)
        {
            try { f.Dispose(); } catch { /* best effort */ }
        }
    }

    /// <summary>Opens a single data file or a directory of data files.</summary>
    public static OstPstSource Open(string path)
    {
        if (File.Exists(path))
        {
            var xst = new XstFile(path);
            // Single file: behaves exactly as before — root is the file's own RootFolder.
            return new OstPstSource([xst], new XstFolderAdapter(xst.RootFolder));
        }

        if (Directory.Exists(path))
        {
            var files = new List<XstFile>();
            var root = new ContainerFolder();
            foreach (string file in FindDataFiles(path))
            {
                try
                {
                    var xst = new XstFile(file);
                    files.Add(xst);
                    // Each file becomes an account folder named after the file; its "Root"
                    // level is collapsed into that name.
                    root.Children.Add(new XstFolderAdapter(
                        xst.RootFolder, Path.GetFileNameWithoutExtension(file)));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  WARN  could not open \"{file}\": {ex.Message}");
                }
            }
            return new OstPstSource(files, root);
        }

        throw new FileNotFoundException($"Source not found: {path}");
    }

    /// <summary>Cheap account list for the confirm prompt — no data file is opened.</summary>
    public static IReadOnlyList<string> ListAccounts(string path)
    {
        if (File.Exists(path))
            return [Path.GetFileNameWithoutExtension(path)];
        if (Directory.Exists(path))
            return FindDataFiles(path).Select(Path.GetFileNameWithoutExtension).ToList()!;
        return [];
    }

    private static List<string> FindDataFiles(string dir)
    {
        var files = new List<string>();
        foreach (string ext in DataFileExtensions)
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(dir, $"*{ext}", SearchOption.AllDirectories));
            }
            catch { /* unreadable subtree — skip */ }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}

/// <summary>Adapts an XstReader <see cref="XstFolder"/> to <see cref="IMailFolder"/>.</summary>
internal sealed class XstFolderAdapter : IMailFolder
{
    private readonly XstFolder _folder;
    private readonly string? _nameOverride;

    /// <param name="nameOverride">
    /// When set, used as this folder's name instead of its display name — used to name a
    /// per-file account folder after the data file.
    /// </param>
    public XstFolderAdapter(XstFolder folder, string? nameOverride = null)
    {
        _folder = folder;
        _nameOverride = nameOverride;
    }

    public string Name
    {
        get
        {
            if (_nameOverride is not null) return _nameOverride;

            string raw = _folder.DisplayName ?? "";
            // IPM_SUBTREE is the raw MAPI name of the mailbox's root mail folder — Outlook
            // hides it behind a friendly label. Give it an intuitive name on disk.
            return string.Equals(raw, "IPM_SUBTREE", StringComparison.OrdinalIgnoreCase)
                ? "Emails"
                : raw;
        }
    }

    public int MessageCount
    {
        get { try { return _folder.ContentCount; } catch { return 0; } }
    }

    public IEnumerable<IMailFolder> SubFolders =>
        _folder.Folders.Select(f => new XstFolderAdapter(f));

    public IEnumerable<IMailMessage> Messages =>
        _folder.Messages.Select(m => new XstMessageAdapter(m));
}

/// <summary>Adapts an XstReader <see cref="XstMessage"/> to <see cref="IMailMessage"/>.</summary>
internal sealed class XstMessageAdapter : IMailMessage
{
    // XstMessage.ClearContentsInternal() is internal — invoked by reflection to release a
    // message's loaded properties/body synchronously the moment we are done with it.
    private static readonly MethodInfo? ClearMethod =
        typeof(XstMessage).GetMethod("ClearContentsInternal",
            BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly XstMessage _msg;

    public XstMessageAdapter(XstMessage msg) => _msg = msg;

    public string? Subject => Safe(() => _msg.Subject);
    public string? From => Safe(() => _msg.From);
    public string? To => Safe(() => _msg.To);
    public string? Cc => Safe(() => _msg.Cc);
    public string? MessageId => Safe(() => _msg.InternetMessageId);
    public DateTime? Date { get { try { return _msg.Date; } catch { return null; } } }

    /// <summary>
    /// Cheap header-only key: subject + both timestamps + sender. These come from the
    /// folder contents table, so reading them does NOT trigger the per-message
    /// decompression that <see cref="ContentHashMaterial"/> needs.
    /// </summary>
    public string QuickKeyMaterial
    {
        get
        {
            var sb = new StringBuilder(256);
            sb.Append("subj:").Append(Safe(() => _msg.Subject)).Append('\n');
            sb.Append("recv:").Append(SafeDate(() => _msg.ReceivedTime)).Append('\n');
            sb.Append("subm:").Append(SafeDate(() => _msg.SubmittedTime)).Append('\n');
            sb.Append("from:").Append(Safe(() => _msg.From)).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>Full content identity — loads the body and an attachment digest.</summary>
    public string ContentHashMaterial
    {
        get
        {
            var sb = new StringBuilder(512);
            sb.Append("imid:").Append(Safe(() => _msg.InternetMessageId)).Append('\n');
            sb.Append("subj:").Append(Safe(() => _msg.Subject)).Append('\n');
            sb.Append("date:").Append(SafeDate(() => _msg.Date)).Append('\n');
            sb.Append("from:").Append(Safe(() => _msg.From)).Append('\n');
            sb.Append("to:").Append(Safe(() => _msg.To)).Append('\n');
            sb.Append("cc:").Append(Safe(() => _msg.Cc)).Append('\n');
            sb.Append("body:").Append(Safe(() => _msg.Body?.Text)).Append('\n');

            sb.Append("att:");
            try
            {
                var atts = _msg.Attachments
                    .Where(a => a.IsFile)
                    .Select(a => $"{a.FileNameForSaving}|{a.Size}")
                    .OrderBy(s => s, StringComparer.Ordinal);
                foreach (string a in atts) sb.Append(a).Append(';');
            }
            catch { /* attachment enumeration can fail on damaged items — ignore */ }
            return sb.ToString();
        }
    }

    public void SaveTo(string path) => new MessageXst(_msg).Save(path);

    /// <summary>
    /// Synchronously drops the message's loaded properties/body/recipients — keeps peak RAM
    /// flat even inside very large folders.
    /// </summary>
    public void Release()
    {
        try { ClearMethod?.Invoke(_msg, null); }
        catch { /* best effort — GC reclaims it later otherwise */ }
    }

    private static string Safe(Func<string?> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafeDate(Func<DateTime?> getter)
    {
        try { return getter()?.ToUniversalTime().ToString("o") ?? string.Empty; }
        catch { return string.Empty; }
    }
}
