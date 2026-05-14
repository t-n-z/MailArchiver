using MimeKit;

namespace MailArchiver;

/// <summary>
/// <see cref="IMailSource"/> over the MIME family of mailbox layouts — a single mbox file,
/// a Thunderbird-style mbox tree, a Maildir, or a directory tree of `.eml` files. All four
/// are read with MimeKit; messages are exported as `.eml`.
/// </summary>
public sealed class MimeSource : IMailSource
{
    private static readonly string[] MboxFileExtensions = ["", ".mbox"];
    private static readonly string[] ThunderbirdStoreDirNames = ["ImapMail", "Mail"];
    private const int ThunderbirdSearchDepth = 3;

    private readonly IMailFolder _root;

    private MimeSource(IMailFolder root) => _root = root;

    public IMailFolder Root => _root;
    public string FileExtension => ".eml";
    public void Dispose() { /* nothing held open between message reads */ }

    public static MimeSource Open(string path, string? formatHint)
    {
        MimeSourceKind kind = SourceDetector.Detect(path, formatHint);
        IMailFolder root = kind switch
        {
            MimeSourceKind.MboxFile => BuildMboxFile(path),
            MimeSourceKind.MboxTree => BuildMboxTree(path),
            MimeSourceKind.Maildir => BuildMaildir(path, Folder(path)),
            MimeSourceKind.EmlDir => BuildEmlDir(path, Folder(path)),
            MimeSourceKind.ThunderbirdProfile => BuildThunderbirdProfile(path),
            _ => throw new InvalidOperationException("unreachable"),
        };
        return new MimeSource(root);
    }

    /// <summary>Cheap account list for the confirm prompt — no mailbox is opened.</summary>
    public static IReadOnlyList<string> ListAccounts(string path, string? formatHint)
    {
        if (SourceDetector.Detect(path, formatHint) == MimeSourceKind.ThunderbirdProfile)
            return FindThunderbirdAccountDirs(path).Select(d => Path.GetFileName(d)).ToList();

        // Single logical account.
        return [File.Exists(path) ? Path.GetFileNameWithoutExtension(path) : Folder(path)];
    }

    // --- Thunderbird profile (many accounts) -------------------------------
    // Mail lives under ImapMail/<server>/ and Mail/<account>/ stores inside the profile.
    // Each account directory is itself a Thunderbird-style mbox tree.

    private static IMailFolder BuildThunderbirdProfile(string root)
    {
        var container = new ContainerFolder();
        foreach (string accountDir in FindThunderbirdAccountDirs(root))
        {
            var account = new MimeFolder { Name = Path.GetFileName(accountDir) };
            AddMboxFolders(accountDir, account);
            container.Children.Add(account);
        }
        return container;
    }

    /// <summary>Every account directory found under ImapMail/ and Mail/ stores in a profile.</summary>
    public static IEnumerable<string> FindThunderbirdAccountDirs(string root)
    {
        foreach (string storeDir in FindStoreDirs(root))
        {
            string[] accounts;
            try { accounts = Directory.GetDirectories(storeDir); }
            catch { continue; }
            foreach (string account in accounts.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                yield return account;
        }
    }

    /// <summary>Directories named ImapMail or Mail, at the path or up to a few levels below it.</summary>
    private static IEnumerable<string> FindStoreDirs(string root)
    {
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            (string dir, int depth) = queue.Dequeue();

            if (ThunderbirdStoreDirNames.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase))
            {
                yield return dir; // a store — don't descend into it here
                continue;
            }
            if (depth >= ThunderbirdSearchDepth) continue;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (string sub in subs)
                queue.Enqueue((sub, depth + 1));
        }
    }

    // --- mbox: single file -------------------------------------------------

    private static MimeFolder BuildMboxFile(string path) => new()
    {
        Name = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } n ? n : "Mailbox",
        MessageSource = () => ParseMbox(path),
        CountSource = () => CountMbox(path),
    };

    // --- mbox: Thunderbird-style tree --------------------------------------
    // A directory of extensionless mbox files; a folder "Inbox" may have a companion
    // "Inbox.sbd" directory holding its subfolders. ".msf" indexes etc. are skipped.

    private static MimeFolder BuildMboxTree(string dir)
    {
        var root = new MimeFolder { Name = Folder(dir) };
        AddMboxFolders(dir, root);
        return root;
    }

    private static void AddMboxFolders(string dir, MimeFolder parent)
    {
        foreach (string file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            if (!MboxFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue; // .msf / .dat / other index files

            var child = new MimeFolder
            {
                Name = name,
                MessageSource = () => ParseMbox(file),
                CountSource = () => CountMbox(file),
            };

            string sbd = Path.Combine(dir, name + ".sbd");
            if (Directory.Exists(sbd))
                AddMboxFolders(sbd, child);

            parent.Children.Add(child);
        }
    }

    // --- Maildir -----------------------------------------------------------
    // A directory with cur/new/tmp; messages live in cur+new. Nested maildirs are
    // subdirectories that themselves contain a "cur" directory.

    private static MimeFolder BuildMaildir(string dir, string name)
    {
        var folder = new MimeFolder
        {
            Name = name,
            MessageSource = () => EnumMaildirFiles(dir),
            CountSource = () => CountMaildirFiles(dir),
        };

        foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string subName = Path.GetFileName(sub);
            if (subName is "cur" or "new" or "tmp") continue;
            if (Directory.Exists(Path.Combine(sub, "cur")))
                folder.Children.Add(BuildMaildir(sub, subName.TrimStart('.')));
        }
        return folder;
    }

    private static IEnumerable<IMailMessage> EnumMaildirFiles(string dir)
    {
        foreach (string sub in new[] { "cur", "new" })
        {
            string subdir = Path.Combine(dir, sub);
            if (!Directory.Exists(subdir)) continue;
            foreach (string file in Directory.EnumerateFiles(subdir))
                yield return new MimeMailMessage(() => MimeMessage.Load(file));
        }
    }

    private static int CountMaildirFiles(string dir)
    {
        int n = 0;
        foreach (string sub in new[] { "cur", "new" })
        {
            string subdir = Path.Combine(dir, sub);
            if (Directory.Exists(subdir))
                n += Directory.EnumerateFiles(subdir).Count();
        }
        return n;
    }

    // --- EML directory tree ------------------------------------------------

    private static MimeFolder BuildEmlDir(string dir, string name)
    {
        var folder = new MimeFolder
        {
            Name = name,
            MessageSource = () => EnumEmlFiles(dir),
            CountSource = () =>
            {
                try { return Directory.EnumerateFiles(dir, "*.eml").Count(); }
                catch { return 0; }
            },
        };

        foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            folder.Children.Add(BuildEmlDir(sub, Path.GetFileName(sub)));

        return folder;
    }

    private static IEnumerable<IMailMessage> EnumEmlFiles(string dir)
    {
        foreach (string file in Directory.EnumerateFiles(dir, "*.eml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            yield return new MimeMailMessage(() => MimeMessage.Load(file));
    }

    // --- mbox parsing helpers ---------------------------------------------

    /// <summary>
    /// Streams messages out of an mbox file. The file stays open for the lifetime of the
    /// enumeration (one message in memory at a time). A parse failure ends this folder's
    /// enumeration rather than risking an infinite loop on a corrupt region.
    /// </summary>
    private static IEnumerable<IMailMessage> ParseMbox(string path)
    {
        // An empty mbox file is a normal, set-up-but-never-used folder (e.g. a fresh
        // "Trash" / "Unsent Messages") — not an error. Yield nothing, say nothing.
        try { if (new FileInfo(path).Length == 0) yield break; }
        catch { /* unreadable size — fall through and let the parser report it */ }

        using FileStream stream = File.OpenRead(path);
        var parser = new MimeParser(stream, MimeFormat.Mbox);
        while (!parser.IsEndOfStream)
        {
            MimeMessage? msg = null;
            try
            {
                msg = parser.ParseMessage();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  WARN  {FolderLabel(path)}: mbox parse stopped: {ex.Message}");
                yield break;
            }
            if (msg is not null)
                yield return new MimeMailMessage(msg);
        }
    }

    /// <summary>Fast message count for an mbox file: lines that begin with the "From " envelope marker.</summary>
    private static int CountMbox(string path)
    {
        try
        {
            int n = 0;
            foreach (string line in File.ReadLines(path))
                if (line.StartsWith("From ", StringComparison.Ordinal))
                    n++;
            return n;
        }
        catch
        {
            return 0; // unreadable for counting — progress just shows a partial total
        }
    }

    private static string Folder(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            is { Length: > 0 } n ? n : "Mailbox";

    /// <summary>
    /// A human label for an mbox file in a warning — "&lt;account-or-parent&gt;/&lt;folder&gt;"
    /// (e.g. "Local Folders/Trash", "imap.example.com/INBOX") so the message names the
    /// account it belongs to.
    /// </summary>
    private static string FolderLabel(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        string parentName = parent is null ? "" : Path.GetFileName(parent);
        string file = Path.GetFileName(path);
        return parentName.Length > 0 ? $"{parentName}/{file}" : file;
    }
}

/// <summary>
/// A uniform folder node for every MIME layout. Each node knows how to lazily enumerate and
/// count its own messages; the tree is built eagerly by <see cref="MimeSource"/>.
/// </summary>
internal sealed class MimeFolder : IMailFolder
{
    public string Name { get; init; } = "";
    public List<MimeFolder> Children { get; } = [];
    public Func<IEnumerable<IMailMessage>> MessageSource { get; init; } = static () => [];
    public Func<int> CountSource { get; init; } = static () => 0;

    public int MessageCount => CountSource();
    public IEnumerable<IMailFolder> SubFolders => Children;
    public IEnumerable<IMailMessage> Messages => MessageSource();
}
