namespace MailArchiver;

/// <summary>
/// Shared program entry logic. Each format-specific executable (Outlook, MIME) calls this
/// with a factory that builds the right <see cref="IMailSource"/> and a cheap lister that
/// names the accounts a source contains (without opening any mailbox). Everything else —
/// argument parsing, the multi-account prompt, supervisor/worker dispatch — is identical.
/// </summary>
public static class EntryPoint
{
    public static int Run(
        string[] args,
        Func<CliArgs, IMailSource> sourceFactory,
        Func<CliArgs, IReadOnlyList<string>> accountLister)
    {
        CliArgs? opts = CliArgs.Parse(args);
        if (opts is null)
            return CliArgs.HelpShown ? 0 : 2; // --help => clean exit; parse error => 2

        if (opts.Worker)
        {
            // Child process: does the actual read/write, reports to the supervisor on stdout.
            using IMailSource source = sourceFactory(opts);
            using var index = new ArchiveIndex(opts.ResolvedDbPath);
            return new Worker(opts, index, source).Run();
        }

        // Supervisor: list accounts cheaply for the confirm prompt, then orchestrate.
        IReadOnlyList<string> accounts;
        try
        {
            accounts = accountLister(opts);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }

        return new Supervisor(opts, accounts).Run();
    }
}
