namespace MailArchiver;

/// <summary>Parsed command-line options for a single backup job.</summary>
public sealed class CliArgs
{
    /// <summary>The mailbox source — a data file (.ost/.pst, .mbox) or a directory
    /// (Thunderbird profile, Maildir, EML tree).</summary>
    public required string SourcePath { get; init; }

    public required string OutDir { get; init; }
    public string? Folder { get; init; }
    public string? DbPath { get; init; }

    /// <summary>Optional source-format override ("mbox", "maildir", "eml"); null = auto-detect.
    /// Only meaningful for MIME sources.</summary>
    public string? Format { get; init; }

    public bool CopyFirst { get; init; }
    public int NameMaxLen { get; init; } = 50;
    public bool Quiet { get; init; }
    public int MsgTimeoutSeconds { get; init; } = 120;
    public int MaxPoison { get; init; } = 100;

    // Internal flags — set by the supervisor when it spawns a worker. Not user-facing.
    public bool Worker { get; init; }
    public bool SkipCount { get; init; }

    public string ResolvedDbPath => DbPath ?? Path.Combine(OutDir, ".mailarchiver.db");

    /// <summary>True when Parse returned null only because --help was requested (clean exit).</summary>
    public static bool HelpShown { get; private set; }

    public static string Usage =>
        """
        MailArchiver — mirrors a mailbox to disk as individual message files.

        Usage:
          MailArchiver --source <path> --out <dir> [options]

        Required:
          --source <path>      Mailbox to read. A data file (.ost/.pst, .mbox) or a
                               directory (Thunderbird profile, Maildir, EML tree).
                               (--datafile is accepted as an alias.)
          --out <dir>          Root output directory for the mirror.

        Options:
          --folder <name|path> Folder to mirror as the root (e.g. "Inbox" or
                               "Inbox/Projects"). Default: the whole store.
          --format <fmt>       Force the source format: thunderbird | mbox | maildir | eml.
                               Default: auto-detect. (MIME sources only.)
          --db <path>          SQLite index file. Default: <out>\.mailarchiver.db
          --copy-first         Copy the source to a temp file before reading
                               (fallback for when it is locked; reads only).
          --name-maxlen <n>    Max base-filename length. Default: 50.
          --msg-timeout <sec>  Kill+skip a message whose read/write stalls this long
                               (a corrupt block can hang a decoder). Default: 120.
          --max-poison <n>     Give up after this many stalled messages. Default: 100.
          --quiet              Suppress live progress; print only the final summary.
          --help               Show this help.

        The source is always opened read-only — this tool can never modify it.
        """;

    /// <summary>Parses args. Returns null and writes to stderr on error or --help.</summary>
    public static CliArgs? Parse(string[] args)
    {
        string? source = null, outDir = null, folder = null, db = null, format = null;
        bool copyFirst = false, quiet = false, worker = false, skipCount = false;
        int nameMaxLen = 50, msgTimeout = 120, maxPoison = 100;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--help" or "-h" or "/?":
                        Console.WriteLine(Usage);
                        HelpShown = true;
                        return null;
                    case "--source" or "--datafile": source = Next(args, ref i, a); break;
                    case "--out": outDir = Next(args, ref i, a); break;
                    case "--folder": folder = Next(args, ref i, a); break;
                    case "--format": format = Next(args, ref i, a); break;
                    case "--db": db = Next(args, ref i, a); break;
                    case "--copy-first": copyFirst = true; break;
                    case "--quiet": quiet = true; break;
                    case "--worker": worker = true; break;       // internal
                    case "--skip-count": skipCount = true; break; // internal
                    case "--name-maxlen":
                        nameMaxLen = ParseInt(Next(args, ref i, a), a, min: 16);
                        break;
                    case "--msg-timeout":
                        msgTimeout = ParseInt(Next(args, ref i, a), a, min: 10);
                        break;
                    case "--max-poison":
                        maxPoison = ParseInt(Next(args, ref i, a), a, min: 0);
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {a}");
                        Console.Error.WriteLine(Usage);
                        return null;
                }
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return null;
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(outDir))
        {
            Console.Error.WriteLine("Both --source and --out are required.");
            Console.Error.WriteLine(Usage);
            return null;
        }
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            Console.Error.WriteLine($"Source not found: {source}");
            return null;
        }

        return new CliArgs
        {
            SourcePath = Path.GetFullPath(source),
            OutDir = Path.GetFullPath(outDir),
            Folder = folder,
            Format = format,
            DbPath = db is null ? null : Path.GetFullPath(db),
            CopyFirst = copyFirst,
            NameMaxLen = nameMaxLen,
            Quiet = quiet,
            MsgTimeoutSeconds = msgTimeout,
            MaxPoison = maxPoison,
            Worker = worker,
            SkipCount = skipCount,
        };
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {flag}");
        return args[++i];
    }

    private static int ParseInt(string raw, string flag, int min)
    {
        if (!int.TryParse(raw, out int value) || value < min)
            throw new ArgumentException($"{flag} must be an integer >= {min} (got '{raw}').");
        return value;
    }
}
