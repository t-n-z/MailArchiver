using MailArchiver;

// MIME archiver: reads mbox (incl. Thunderbird profiles), Maildir, and EML directories via
// MimeKit, writes .eml. --source may be a single mailbox or a whole Thunderbird profile
// folder. Everything else (supervisor/worker, index, resume, progress, viewer) lives in
// MailArchiver.Core and is shared with the Outlook archiver.
return EntryPoint.Run(
    args,
    sourceFactory: opts => MimeSource.Open(opts.SourcePath, opts.Format),
    accountLister: opts => MimeSource.ListAccounts(opts.SourcePath, opts.Format));
