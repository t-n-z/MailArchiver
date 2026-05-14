namespace MailArchiver;

/// <summary>The MIME source layouts the archiver can read.</summary>
public enum MimeSourceKind
{
    /// <summary>A single mbox file.</summary>
    MboxFile,
    /// <summary>A Thunderbird-style directory: extensionless mbox files + ".sbd" subfolder dirs.</summary>
    MboxTree,
    /// <summary>A Maildir: a directory containing "cur"/"new"/"tmp".</summary>
    Maildir,
    /// <summary>A directory tree of ".eml" files.</summary>
    EmlDir,
    /// <summary>A Thunderbird profile (or a folder above it): one or more ImapMail/Mail account stores.</summary>
    ThunderbirdProfile,
}

/// <summary>Works out which MIME layout a source path is, honoring an optional --format override.</summary>
public static class SourceDetector
{
    public static MimeSourceKind Detect(string path, string? formatHint)
    {
        if (!string.IsNullOrWhiteSpace(formatHint))
        {
            return formatHint.Trim().ToLowerInvariant() switch
            {
                "thunderbird" => MimeSourceKind.ThunderbirdProfile,
                "mbox" => File.Exists(path) ? MimeSourceKind.MboxFile : MimeSourceKind.MboxTree,
                "maildir" => MimeSourceKind.Maildir,
                "eml" => MimeSourceKind.EmlDir,
                _ => throw new ArgumentException(
                    $"Unknown --format \"{formatHint}\". Use: thunderbird | mbox | maildir | eml."),
            };
        }

        if (File.Exists(path))
            return MimeSourceKind.MboxFile; // a single file source is treated as an mbox

        // Directory source — sniff the layout.
        if (Directory.Exists(Path.Combine(path, "cur")) && Directory.Exists(Path.Combine(path, "new")))
            return MimeSourceKind.Maildir;

        // A Thunderbird profile has ImapMail/ and/or Mail/ stores somewhere below it.
        // Checked before the generic mbox-tree sniff so that pointing directly at a single
        // account directory still resolves to MboxTree.
        if (MimeSource.FindThunderbirdAccountDirs(path).Any())
            return MimeSourceKind.ThunderbirdProfile;

        bool hasSbd = Directory.EnumerateDirectories(path, "*.sbd").Any();
        bool hasMsf = Directory.EnumerateFiles(path, "*.msf").Any();
        bool hasExtensionlessFiles = Directory.EnumerateFiles(path)
            .Any(f => string.IsNullOrEmpty(Path.GetExtension(f)) && !Path.GetFileName(f).StartsWith('.'));
        if (hasSbd || hasMsf || hasExtensionlessFiles)
            return MimeSourceKind.MboxTree;

        if (Directory.EnumerateFiles(path, "*.eml", SearchOption.AllDirectories).Any())
            return MimeSourceKind.EmlDir;

        throw new InvalidOperationException(
            $"Could not detect the mailbox format of \"{path}\". " +
            "Pass --format thunderbird|mbox|maildir|eml to say which it is.");
    }
}
