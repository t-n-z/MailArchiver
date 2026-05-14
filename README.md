# MailArchiver

Mirrors a mailbox to disk as individual message files, preserving the folder structure —
an additive, resumable, scriptable backup. The source is always opened **read-only**; the
tool can never modify it. The output is **plain, unencrypted files** — see *Security* below.

## Why this exists

Email in an Outlook `.ost`/`.pst` or a Thunderbird profile is locked inside an opaque
database file. You can't browse it in File Explorer, search it with ordinary tools, pull a
single message out, or hand it to anything else without the original client. MailArchiver
unpacks that database into a normal folder tree — one file per email, the mailbox's folder
structure mirrored — so the whole mailbox becomes ordinary files you can browse, search,
copy and keep like anything else.

**The main use case — and what the author uses it for —** is offloading mail from free or
storage-capped email accounts. Free providers cap your space; when you hit the cap you
either pay, pay *more* if you're already paying, or delete mail. MailArchiver lets you pull
a full local copy first, then delete from the server with the originals safely archived on
disk — no subscription, no upgrade.

Why not just copy the `.ost` file itself? Because OST files corrupt — the author has had it
happen, and reversing OST corruption is difficult to impossible. Extracting each message as
its own file means a single bad message can't take the rest down with it, and the backup is
not hostage to one fragile container.

Paired with an email client it supports **fast, scheduled backups** to a local drive or a
cloud-synced folder.

## The backup model — additive, one-way, ever-expanding

Understand this before relying on it:

- The **first run is a snapshot** of the mailbox at that moment in time.
- **Every later run is a one-way sync**: new messages are added; **nothing is ever removed**.
  Delete an email from the mailbox and its archived copy stays.
- It is **add-only by design** — there is no two-way sync and deletions are never mirrored.
- A consequence: if you **move an email between folders**, the next run sees it as new in
  the new location, so it ends up archived in *both* places. (Smarter move-detection may
  come in a future update.)

The result is an **individualised, ever-expanding image of the mailbox** — every message
that has ever been in it, each as a separate file, as lossless as the format allows. It
only grows.

## Two executables

| Executable | Reads | Writes |
|------------|-------|--------|
| `MailArchiver-Outlook.exe` | Outlook `.ost` / `.pst` (incl. modern 4K-format OST) | `.msg` |
| `MailArchiver-Mime.exe` | mbox, Thunderbird profiles, Maildir, EML directories | `.eml` |

Both take a **single file** or a **folder of accounts**. Point the Outlook tool at a folder
and it archives every `.ost`/`.pst` inside; point the MIME tool at a Thunderbird profile and
it archives every account under `ImapMail/` and `Mail/`. With more than one account it lists
them and asks before proceeding — once: it remembers your answer in the index and only asks
again if the set of accounts changes.

Every backup is **self-browsing**: a small `EmailArchiveViewer.exe` is dropped into the
backup root — a one-window viewer (folder dropdown, message list, detail pane) that reads
both `.msg` and `.eml`. It needs the .NET 9 Desktop Runtime installed.

## Download and run

MailArchiver is a **command-line tool** — you don't double-click it, you run it from a
terminal with a couple of arguments. It's still simple; this is the whole process.

### 1. Download the right executable

Go to the **[Releases](https://github.com/t-n-z/MailArchiver/releases)** page — *not* the
green "Code" button (that's the source code). Under the latest release, download:

- **`MailArchiver-Outlook.exe`** — if your mail is in Outlook.
- **`MailArchiver-Mime.exe`** — if your mail is in Thunderbird, or you have `.mbox` /
  Maildir / `.eml` files.

Each is a single self-contained file — no installer, nothing else to grab. (The *viewer*
that lands in your backup needs the free
[.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) if you want to
browse the backup later; the archiver itself needs nothing.)

Put the `.exe` somewhere easy, e.g. `C:\MailArchiver\`.

### 2. Find your mailbox

**Outlook** — data files live in
`C:\Users\<you>\AppData\Local\Microsoft\Outlook\`. Quick way: paste
`%LOCALAPPDATA%\Microsoft\Outlook` into the File Explorer address bar. You'll see one or
more `.ost` files (sometimes `.pst`); standalone `.pst` files are often in
`Documents\Outlook Files\` instead.

**Thunderbird** — your profile lives in
`C:\Users\<you>\AppData\Roaming\Thunderbird\Profiles\`. Quick way: paste
`%APPDATA%\Thunderbird\Profiles` into the address bar. Inside is a folder like
`abcd1234.default-release` — that whole folder is what you point MailArchiver at.

**Close Outlook / Thunderbird before running** — they lock their files while open. If you
can't close them, add `--copy-first` and MailArchiver works from a temporary copy instead.

### 3. Get the paths the easy way

You need two paths: the mailbox (`--source`) and where the backup goes (`--out`).

- **Shift + Right-click** a file or folder in File Explorer → **"Copy as path"** — gives
  you the full path already in quotes, ready to paste.
- **Shift + Right-click** an empty area inside a folder → **"Open PowerShell window here"** —
  opens a terminal already sitting in that folder, so you don't have to type your way to it.

### 4. Run it

Open a terminal in the folder where you put the `.exe` (Shift + Right-click → "Open
PowerShell window here", or press `Win+R` and type `cmd`). Then run it — for example, an
Outlook user backing up one account to an external drive:

```
MailArchiver-Outlook.exe --source "C:\Users\Tom\AppData\Local\Microsoft\Outlook\tom@example.com.ost" --out "E:\Mail Backup\tom@example.com"
```

A Thunderbird user backing up their whole profile (every account at once):

```
MailArchiver-Mime.exe --source "C:\Users\Tom\AppData\Roaming\Thunderbird\Profiles\abcd1234.default-release" --out "E:\Mail Backup\thunderbird"
```

### What happens next

It opens the mailbox **read-only**, walks every folder, and writes each email as its own
file under `--out`, mirroring the folder layout — e.g.
`E:\Mail Backup\tom@example.com\...\Inbox\20250512_103000_Alice_Bob_Project_update.msg`.
A live progress line shows how it's going; at the end it prints a summary. You'll find an
`EmailArchiveViewer.exe` sitting in the backup root — double-click that any time to browse
what was saved.

**Re-run it whenever you like.** It only *adds* new mail and never touches what's already
saved, so re-running (or scheduling it — see *Scheduling* below) just tops the backup up.
Once you've confirmed a backup looks complete and correct, you can safely delete mail from
the server to free up space.

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

## Scheduling

Because re-running only adds new mail, MailArchiver is meant to be run on a schedule. Put
your commands in a `.bat` file — lines run one after another, each finishing before the
next starts:

```bat
@echo off
MailArchiver-Outlook.exe --source "C:\...\account1.ost"        --out "E:\Mail Backup\account1" --quiet
MailArchiver-Mime.exe    --source "C:\...\Thunderbird profile" --out "E:\Mail Backup\thunderbird" --quiet
```

Point Windows **Task Scheduler** at that `.bat` to run it daily/weekly. For unattended runs
add `< nul` after a command so it never waits at a prompt (a locked file then just skips
that line instead of asking). `--quiet` keeps the log short.

## Security — the backup is NOT encrypted

**Read this.** Mailbox source files are generally not encrypted, and **none of
MailArchiver's output is encrypted either**. Every archived message is written as a plain,
standalone, openable file — deliberately. That is the entire point: a raw, inspectable
image of your mail that depends on no container and no tool to read, with each email kept
as an individual item rather than wrapped or locked away.

That design choice means **the security of the backup is entirely the security of where
you put it**. MailArchiver is meant to be run **inside an already-secure environment** — an
encrypted disk, a locked-down machine, an access-controlled folder. The program itself does
nothing to protect the contents. Treat a MailArchiver output folder exactly as sensitively
as the mailbox it came from.

## How it works

The real read and write happen in a child **worker** process supervised by a parent. If a
format decoder hangs on a corrupt block (which native decompression can do, holding a lock
that can't be interrupted in-process), the supervisor kills the worker, records the
offending message so it's skipped, and respawns to continue. Output is written
`.tmp`-then-atomically-moved; a SQLite index is committed at checkpoints; a run marker
detects unclean shutdowns and guards against two runs writing the same output. The source
is read-only throughout; the backup is additive and unencrypted (see the sections above).

## How it works — in detail

**Process isolation.** A corrupt or pathological block can make a native decoder hang in a
way that cannot be interrupted from inside the process (e.g. decompression spinning while
holding a stream lock). MailArchiver therefore does the actual mailbox reading and message
writing in a **child worker process**, watched by a **supervisor** parent over a simple
heartbeat protocol. If the worker stops reporting progress for `--msg-timeout` seconds, the
supervisor kills it, records that one message as *poisoned* in the index so it is skipped
from then on, and respawns the worker to carry on. `--max-poison` caps how many such
messages it will tolerate before giving up.

**Atomic, additive writes.** Each message is written to a `.tmp` file and then atomically
moved into place, so an interrupted write never leaves a half-written message. Existing
files are never overwritten or deleted — the backup only ever gains files.

**The index and resuming.** A SQLite database (`.mailarchiver.db`) in the output root
records what has already been archived, using a cheap header-only key so a re-run can skip
an already-saved message without re-reading its body. The index is committed at checkpoints
during the run; a **run marker** file records that a run is in progress, lets the next run
detect an unclean shutdown and resume from the last checkpoint, and prevents two runs from
writing the same output at once.

**Multi-account.** When `--source` is a folder containing several accounts (an Outlook data
folder, a Thunderbird profile), each account becomes a top-level folder in the output. The
first such run lists the accounts and asks for confirmation; the confirmed set is stored in
the index, so routine re-runs don't ask again unless an account is added or removed.

**Lossless intent.** Outlook messages are written as `.msg` (the native Outlook format) and
MIME-family messages as `.eml`, in both cases aiming to preserve the message as faithfully
as the format allows — the goal is an individualised, lossless-as-possible image of the
mailbox, not a re-rendering of it.

**The embedded viewer.** Both archivers carry a small WinForms viewer
(`EmailArchiveViewer.exe`) embedded as a resource and drop it into the backup root, so any
backup is browsable on its own without the archiver. It reads both `.msg` and `.eml`.

## Building

Requires the .NET 9 SDK.

```powershell
.\build.ps1
```

Produces both self-contained, single-file executables at the repo root, each with the
viewer embedded. (`build.ps1` copies them to the root for convenience — they are gitignored
and meant to be distributed via GitHub Releases.)

## Disclaimer

MailArchiver is provided **"as is", with no warranty of any kind** (see `LICENSE`). It is a
backup *aid*, not a guaranteed backup system.

- **All responsibility for your data lies with you.** You are responsible for verifying
  that a backup is complete and correct, for storing it somewhere safe, and for testing
  that you can actually read it back.
- **The author accepts no liability for any loss, corruption, or exposure of information** —
  in the source mailbox, in the backup output, or anywhere else — arising from the use,
  misuse, or failure of this software.
- The source is opened read-only and the program is written defensively, but no software is
  perfect. **Do not delete anything from a mailbox until you have independently confirmed
  the archive is good.**
- You are responsible for the security of the (unencrypted) output — see *Security* above.

If you are not comfortable with these terms, do not use this software.

## Licensing

MailArchiver's own code is MIT (see `LICENSE`). It vendors a patched copy of
[XstReader](https://github.com/iluvadev/XstReader) (Ms-PL) and the MsgKit subtree (MIT), and
references MimeKit, MSGReader, OpenMcdf and others — all permissive. See
`THIRD-PARTY-NOTICES.md`.
