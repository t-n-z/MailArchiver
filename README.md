# MailArchiver

Mirrors a mailbox to disk as individual message files, preserving the folder structure —
an additive, resumable, scriptable backup. The source is always opened **read-only**; the
tool can never modify it.

## Two executables

| Executable | Reads | Writes |
|------------|-------|--------|
| `MailArchiver-Outlook.exe` | Outlook `.ost` / `.pst` (incl. modern 4K-format OST) | `.msg` |
| `MailArchiver-Mime.exe` | mbox, Thunderbird profiles, Maildir, EML directories | `.eml` |

Both take a **single file** or a **folder of accounts**. Point the Outlook tool at a folder
and it archives every `.ost`/`.pst` inside; point the MIME tool at a Thunderbird profile and
it archives every account under `ImapMail/` and `Mail/`. With more than one account it lists
them and asks before proceeding.

Every backup is **self-browsing**: a small `EmailArchiveViewer.exe` is dropped into the
backup root — a one-window viewer (folder dropdown, message list, detail pane) that reads
both `.msg` and `.eml`. It needs the .NET 9 Desktop Runtime installed.

## Usage

```
MailArchiver-Outlook.exe --source <path> --out <dir> [options]
MailArchiver-Mime.exe    --source <path> --out <dir> [options]

Required:
  --source <path>      Mailbox to read — a data file (.ost/.pst, .mbox) or a directory
                       (Outlook data folder, Thunderbird profile, Maildir, EML tree).
  --out <dir>          Root output directory for the mirror.

Options:
  --folder <name|path> Folder to mirror as the root (e.g. "Inbox"). Default: whole store.
  --format <fmt>       Force the source format: thunderbird | mbox | maildir | eml.
                       Default: auto-detect. (MIME tool only.)
  --db <path>          SQLite index file. Default: <out>\.mailarchiver.db
  --copy-first         Copy the source to a temp file before reading (for locked files).
  --name-maxlen <n>    Max base-filename length. Default: 50.
  --msg-timeout <sec>  Kill+skip a message whose read/write stalls this long. Default: 120.
  --max-poison <n>     Give up after this many stalled messages. Default: 100.
  --quiet              Suppress live progress; print only the final summary.
  --help               Show this help.
```

Exit codes: `0` ok, `1` finished with per-message errors, `2` fatal.

Re-running is safe — already-archived messages are skipped via a SQLite index, and an
interrupted run resumes from its last checkpoint. Nothing is ever deleted from a backup.

## How it works

The real read/write happens in a child **worker** process supervised by a parent. If a
format decoder hangs on a corrupt block (which native decompression can do, holding a lock
that can't be interrupted in-process), the supervisor kills the worker, records the
offending message so it's skipped, and respawns to continue. Output is written
`.tmp`-then-atomically-moved; the index is committed at checkpoints; a run marker detects
unclean shutdowns and guards against two runs writing the same output.

## Building

Requires the .NET 9 SDK.

```powershell
.\build.ps1
```

Produces both self-contained, single-file executables at the repo root, each with the
viewer embedded. (`build.ps1` copies them to the root for convenience — they are gitignored
and meant to be distributed via GitHub Releases.)

## Licensing

MailArchiver's own code is MIT (see `LICENSE`). It vendors a patched copy of
[XstReader](https://github.com/iluvadev/XstReader) (Ms-PL) and the MsgKit subtree (MIT), and
references MimeKit, MSGReader, OpenMcdf and others — all permissive. See
`THIRD-PARTY-NOTICES.md`.
