using Microsoft.Data.Sqlite;

namespace MailArchiver;

/// <summary>
/// SQLite-backed incremental index — also the durable progress ledger. Each row is a
/// message that has been fully written to disk. Re-runs and resumed runs skip rows that
/// are already here.
///
/// Keyed by (folder_path, content_hash): one row per distinct email content per mirrored
/// folder. Dedup is folder-scoped on purpose — an email moved between folders produces a
/// new row (and a duplicate file in the new folder), the desired "messy mirror" behavior.
///
/// A cheap_key (hash of immutable header fields, no message body) lets a re-run skip a
/// message without paying the expensive body load that content_hash needs.
///
/// Writes happen inside a batch transaction that is committed at every checkpoint, so an
/// interrupted run loses at most the work since the last checkpoint.
/// </summary>
public sealed class ArchiveIndex : IDisposable
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction? _tx;

    public ArchiveIndex(string dbPath)
    {
        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _conn.Open();

        Exec("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS archived_messages (
              folder_path  TEXT NOT NULL,
              content_hash TEXT NOT NULL,
              cheap_key    TEXT,
              file_path    TEXT NOT NULL,
              poisoned     INTEGER NOT NULL DEFAULT 0,
              entry_id     TEXT,
              internet_id  TEXT,
              subject      TEXT,
              sent_utc     TEXT,
              written_utc  TEXT NOT NULL,
              PRIMARY KEY (folder_path, content_hash)
            );
            CREATE INDEX IF NOT EXISTS ix_hash  ON archived_messages(content_hash);
            CREATE INDEX IF NOT EXISTS ix_path  ON archived_messages(file_path);
            CREATE INDEX IF NOT EXISTS ix_cheap ON archived_messages(folder_path, cheap_key);
            CREATE TABLE IF NOT EXISTS settings (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """);

        // Forward-compatibility for index DBs written by an earlier pre-release build that
        // lacked cheap_key / poisoned. For a DB created by the CREATE TABLE above these
        // ALTERs are redundant no-ops (swallowed by TryExec); they exist only so an older
        // backup's .mailarchiver.db keeps working after an upgrade.
        TryExec("ALTER TABLE archived_messages ADD COLUMN cheap_key TEXT;");
        TryExec("ALTER TABLE archived_messages ADD COLUMN poisoned INTEGER NOT NULL DEFAULT 0;");
        TryExec("CREATE INDEX IF NOT EXISTS ix_cheap ON archived_messages(folder_path, cheap_key);");
    }

    /// <summary>
    /// Fast pre-check: is a message with this cheap_key already known for this folder?
    /// Lets the caller skip without loading the message body. <paramref name="poisoned"/>
    /// is true if the row marks a message that previously stalled and must not be retried.
    /// </summary>
    public bool TryGetByCheapKey(string folderPath, string cheapKey, out string filePath, out bool poisoned)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            "SELECT file_path, poisoned FROM archived_messages " +
            "WHERE folder_path=$f AND cheap_key=$k ORDER BY poisoned DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$f", folderPath);
        cmd.Parameters.AddWithValue("$k", cheapKey);
        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read())
        {
            filePath = string.Empty;
            poisoned = false;
            return false;
        }
        filePath = r.IsDBNull(0) ? string.Empty : r.GetString(0);
        poisoned = r.GetInt64(1) != 0;
        return true;
    }

    /// <summary>
    /// Records that a message stalled (e.g. a corrupt block hung decompression) so future
    /// runs skip it instead of hanging again. Written by the supervisor after it kills a
    /// stuck worker — the supervisor opens its own short-lived <see cref="ArchiveIndex"/>
    /// for this, so it runs while no worker holds the DB. Like every method here it uses
    /// the instance connection and the current batch transaction (if one is open).
    /// </summary>
    public void MarkPoisoned(string folderPath, string cheapKey, string reason)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO archived_messages
              (folder_path, content_hash, cheap_key, file_path, poisoned, written_utc)
            VALUES ($f, $h, $k, $p, 1, $w);
            """;
        cmd.Parameters.AddWithValue("$f", folderPath);
        cmd.Parameters.AddWithValue("$h", "POISON:" + cheapKey);
        cmd.Parameters.AddWithValue("$k", cheapKey);
        cmd.Parameters.AddWithValue("$p", reason);
        cmd.Parameters.AddWithValue("$w", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>True if this exact content is already recorded for this folder.</summary>
    public bool TryGetArchived(string folderPath, string contentHash, out string filePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            "SELECT file_path FROM archived_messages WHERE folder_path=$f AND content_hash=$h LIMIT 1;";
        cmd.Parameters.AddWithValue("$f", folderPath);
        cmd.Parameters.AddWithValue("$h", contentHash);
        object? result = cmd.ExecuteScalar();
        filePath = result as string ?? string.Empty;
        return result is not null;
    }

    public void Record(string folderPath, string contentHash, string cheapKey, string filePath,
        string? entryId, string? internetId, string? subject, DateTime? sentUtc)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO archived_messages
              (folder_path, content_hash, cheap_key, file_path, entry_id, internet_id, subject, sent_utc, written_utc)
            VALUES ($f, $h, $k, $p, $e, $i, $s, $sent, $w);
            """;
        cmd.Parameters.AddWithValue("$f", folderPath);
        cmd.Parameters.AddWithValue("$h", contentHash);
        cmd.Parameters.AddWithValue("$k", cheapKey);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.Parameters.AddWithValue("$e", (object?)entryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$i", (object?)internetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sent", (object?)sentUtc?.ToUniversalTime().ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$w", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads a value from the small key/value <c>settings</c> table; null if absent.</summary>
    public string? GetSetting(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Writes (inserts or replaces) a value in the <c>settings</c> table.</summary>
    public void SetSetting(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Total archived (non-poisoned) rows and poisoned rows — for the final summary.</summary>
    public (long archived, long poisoned) GetStats()
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            "SELECT SUM(CASE WHEN poisoned=0 THEN 1 ELSE 0 END), " +
            "       SUM(CASE WHEN poisoned=1 THEN 1 ELSE 0 END) FROM archived_messages;";
        using SqliteDataReader r = cmd.ExecuteReader();
        if (r.Read())
            return (r.IsDBNull(0) ? 0 : r.GetInt64(0), r.IsDBNull(1) ? 0 : r.GetInt64(1));
        return (0, 0);
    }

    /// <summary>Starts a batch transaction; call <see cref="Checkpoint"/> or <see cref="CommitBatch"/> to flush.</summary>
    public void BeginBatch() => _tx ??= _conn.BeginTransaction();

    /// <summary>Durably commits everything written so far and immediately opens a fresh batch.</summary>
    public void Checkpoint()
    {
        CommitBatch();
        BeginBatch();
    }

    public void CommitBatch()
    {
        _tx?.Commit();
        _tx?.Dispose();
        _tx = null;
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void TryExec(string sql)
    {
        try { Exec(sql); }
        catch (SqliteException) { /* already applied (e.g. duplicate column) */ }
    }

    public void Dispose()
    {
        _tx?.Dispose();
        _conn.Dispose();
    }
}
