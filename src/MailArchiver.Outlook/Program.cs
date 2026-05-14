using MailArchiver;

// Outlook archiver: reads .ost/.pst via the vendored XstReader, writes .msg via MsgKit.
// --source may be a single data file or a folder of them. Everything else
// (supervisor/worker, index, resume, progress, viewer) lives in MailArchiver.Core and is
// shared with the MIME archiver.
return EntryPoint.Run(
    args,
    sourceFactory: opts => OstPstSource.Open(opts.SourcePath),
    accountLister: opts => OstPstSource.ListAccounts(opts.SourcePath));
