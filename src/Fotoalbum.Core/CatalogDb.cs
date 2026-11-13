using Microsoft.Data.Sqlite;

namespace Fotoalbum.Core;

public enum PhotoStatus { None, Draft, Done, Manual, Skip, MarkedDelete }

/// <summary>Původ popisku: AI korektura, ruční text bez korektury, nebo převzatý ze souboru.</summary>
public static class CaptionSource
{
    public const string Ai = "ai";
    public const string Manual = "manual";
    public const string File = "file";
}

public sealed class PhotoRecord
{
    public required string Path { get; init; }
    public string Caption { get; set; } = "";
    /// <summary>Text je rozpracovaný (např. surový diktát) a ještě se nezapisuje do fotky.</summary>
    public bool IsDraft { get; set; }
    public string RawDictation { get; set; } = "";
    public bool Skip { get; set; }
    public bool MarkedDelete { get; set; }
    public int Rotation { get; set; }
    /// <summary>null = podle složky, true/false = ruční výjimka.</summary>
    public bool? Everyday { get; set; }
    public string? DateTaken { get; set; }
    public long FileSize { get; set; }
    public long FileMtime { get; set; }
    public bool Imported { get; set; }
    /// <summary>Popisek, který je naposledy prokazatelně zapsaný ve fotce.</summary>
    public string? FileCaption { get; set; }
    public string? WriteError { get; set; }
    /// <summary>Původ popisku (<see cref="Core.CaptionSource"/>).</summary>
    public string CaptionSource { get; set; } = Core.CaptionSource.File;
    /// <summary>Krátký nadpis od AI (null = první věta popisku).</summary>
    public string? Title { get; set; }

    public PhotoStatus Status =>
        MarkedDelete ? PhotoStatus.MarkedDelete
        : Caption.Trim().Length > 0 ? (IsDraft ? PhotoStatus.Draft
            : CaptionSource == Core.CaptionSource.Manual ? PhotoStatus.Manual : PhotoStatus.Done)
        : Skip ? PhotoStatus.Skip
        : PhotoStatus.None;

    /// <summary>Popisek čeká na zápis do souboru.</summary>
    public bool NeedsFileWrite => Imported && !IsDraft && Caption != (FileCaption ?? "");
}

public sealed record HistoryEntry(long Id, string Caption, bool IsDraft, string CreatedAt, string Source);

/// <summary>
/// Lokální databáze aplikace (SQLite). Drží vše, co není v metadatech fotky:
/// surový diktát, historii verzí, stavy, poslední pozice. Popisek je navíc vždy zapsán i ve fotce.
/// </summary>
public sealed class CatalogDb : IDisposable
{
    private const int SchemaVersion = 3;
    private static readonly TimeSpan HistoryMergeWindow = TimeSpan.FromSeconds(60);
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public CatalogDb(string file)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Migrate();
    }

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version"));
        if (version >= SchemaVersion) return;
        if (version < 1) CreateV1();
        if (version < 2)
        {
            // verze 2: původ popisku (AI / ruční / ze souboru) a nadpis od AI
            Exec("ALTER TABLE photos ADD COLUMN caption_source TEXT NOT NULL DEFAULT 'file'");
            Exec("ALTER TABLE photos ADD COLUMN title TEXT NULL");
        }
        if (version < 3)
            Exec("CREATE TABLE IF NOT EXISTS album_intros(path TEXT PRIMARY KEY COLLATE NOCASE, intro TEXT NOT NULL)");
        Exec($"PRAGMA user_version={SchemaVersion}");
    }

    private void CreateV1()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS photos(
              path TEXT PRIMARY KEY COLLATE NOCASE,
              caption TEXT NOT NULL DEFAULT '',
              is_draft INTEGER NOT NULL DEFAULT 0,
              raw_dictation TEXT NOT NULL DEFAULT '',
              skip INTEGER NOT NULL DEFAULT 0,
              marked_delete INTEGER NOT NULL DEFAULT 0,
              rotation INTEGER NOT NULL DEFAULT 0,
              everyday INTEGER NULL,
              date_taken TEXT NULL,
              file_size INTEGER NOT NULL DEFAULT 0,
              file_mtime INTEGER NOT NULL DEFAULT 0,
              imported INTEGER NOT NULL DEFAULT 0,
              file_caption TEXT NULL,
              write_error TEXT NULL,
              updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')));
            CREATE TABLE IF NOT EXISTS caption_history(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              path TEXT NOT NULL COLLATE NOCASE,
              caption TEXT NOT NULL,
              is_draft INTEGER NOT NULL,
              source TEXT NOT NULL,
              created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_history_path ON caption_history(path, id);
            CREATE TABLE IF NOT EXISTS folders(
              path TEXT PRIMARY KEY COLLATE NOCASE,
              kind TEXT NULL,
              last_photo TEXT NULL);
            CREATE TABLE IF NOT EXISTS app_state(key TEXT PRIMARY KEY, value TEXT NULL);
            """);
    }

    // ---------- fotky ----------

    public Dictionary<string, PhotoRecord> GetPhotos(IEnumerable<string> paths)
    {
        var result = new Dictionary<string, PhotoRecord>(StringComparer.OrdinalIgnoreCase);
        var list = paths.ToList();
        lock (_lock)
        {
            foreach (var chunk in list.Chunk(400))
            {
                using var cmd = _db.CreateCommand();
                var names = new List<string>();
                for (var i = 0; i < chunk.Length; i++)
                {
                    names.Add("$p" + i);
                    cmd.Parameters.AddWithValue("$p" + i, chunk[i]);
                }
                cmd.CommandText = $"SELECT * FROM photos WHERE path IN ({string.Join(',', names)})";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { var rec = Read(r); result[rec.Path] = rec; }
            }
        }
        return result;
    }

    public PhotoRecord? GetPhoto(string path) => GetPhotos([path]).GetValueOrDefault(path);

    public PhotoRecord GetOrCreate(string path) => GetPhoto(path) ?? new PhotoRecord { Path = path };

    /// <summary>
    /// Zapíše údaje načtené ze souboru. Pokud uživatel v aplikaci mezitím popisek neměnil,
    /// převezme se popisek ze souboru (např. když ho někdo upravil jiným programem).
    /// </summary>
    public void ApplyImport(string path, string? fileCaption, string? dateTaken, long size, long mtime)
    {
        lock (_lock)
        {
            var rec = GetPhotos([path]).GetValueOrDefault(path);
            var cleaned = fileCaption?.Trim() ?? "";
            if (rec is null)
            {
                rec = new PhotoRecord { Path = path, Caption = cleaned };
                if (cleaned.Length > 0) AddHistoryLocked(path, cleaned, false, "soubor", merge: false);
            }
            else
            {
                var userUntouched = rec.Caption == (rec.FileCaption ?? "") || !rec.Imported && rec.Caption.Length == 0;
                if (userUntouched && !rec.IsDraft && rec.Caption != cleaned)
                {
                    rec.Caption = cleaned;
                    rec.CaptionSource = CaptionSource.File;
                    rec.Title = null;
                    AddHistoryLocked(path, cleaned, false, "soubor", merge: false);
                }
            }
            rec.FileCaption = cleaned;
            rec.DateTaken = dateTaken;
            rec.FileSize = size;
            rec.FileMtime = mtime;
            rec.Imported = true;
            Upsert(rec);
        }
    }

    public PhotoRecord SaveCaption(string path, string caption, bool isDraft, string source = "uzivatel",
        string captionSource = CaptionSource.Manual, string? title = null, string? rawDictation = null)
    {
        lock (_lock)
        {
            var rec = GetOrCreate(path);
            caption = caption.Replace("\r\n", "\n").TrimEnd();
            if (rec.Caption == caption && rec.IsDraft == isDraft && rec.CaptionSource == captionSource && rec.Title == title
                && (rawDictation is null || rec.RawDictation == rawDictation)) return rec;
            if (rawDictation is not null) rec.RawDictation = rawDictation.Trim();
            rec.Caption = caption;
            rec.IsDraft = isDraft;
            rec.CaptionSource = captionSource;
            rec.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            if (caption.Length > 0) rec.Skip = false;
            Upsert(rec);
            AddHistoryLocked(path, caption, isDraft, source, merge: true);
            return rec;
        }
    }

    public PhotoRecord SetFlags(string path, bool? skip = null, bool? markedDelete = null, int? rotation = null,
        bool? everyday = null, bool clearEveryday = false)
    {
        lock (_lock)
        {
            var rec = GetOrCreate(path);
            if (skip is { } s) rec.Skip = s;
            if (markedDelete is { } m) rec.MarkedDelete = m;
            if (rotation is { } r) rec.Rotation = ((r % 360) + 360) % 360;
            if (everyday is { } e) rec.Everyday = e;
            if (clearEveryday) rec.Everyday = null;
            Upsert(rec);
            return rec;
        }
    }

    /// <summary>Hromadná změna příznaku Skip v jedné transakci. Při vrácení se zachovají mezitím upravené fotky.</summary>
    public (string Batch, List<string> Paths) SetFolderSkipped(List<string> paths, string? undoBatch)
    {
        lock (_lock)
        {
            var records = GetPhotos(paths);
            var batch = DateTime.UtcNow.ToString("O");
            using var tx = _db.BeginTransaction();
            using var cmd = Cmd(undoBatch is null
                ? """
                    INSERT INTO photos(path, skip, updated_at) VALUES($p, 1, $now)
                    ON CONFLICT(path) DO UPDATE SET skip=1, updated_at=$now
                    """
                : "UPDATE photos SET skip=0, updated_at=$now WHERE path=$p AND updated_at=$batch AND skip=1");
            cmd.Transaction = tx;
            var pathParameter = cmd.Parameters.AddWithValue("$p", "");
            cmd.Parameters.AddWithValue("$now", batch);
            if (undoBatch is not null) cmd.Parameters.AddWithValue("$batch", undoBatch);

            var changed = new List<string>();
            foreach (var path in paths)
            {
                var record = records.GetValueOrDefault(path);
                if (undoBatch is null
                    ? record is not null && (record.Status != PhotoStatus.None || record.IsDraft || record.RawDictation.Length > 0)
                    : record?.Status != PhotoStatus.Skip)
                    continue;
                pathParameter.Value = path;
                if (cmd.ExecuteNonQuery() != 0) changed.Add(path);
            }
            tx.Commit();
            return (batch, changed);
        }
    }

    public void MarkWritten(string path, string writtenCaption, long size, long mtime)
    {
        lock (_lock)
        {
            using var cmd = Cmd("UPDATE photos SET file_caption=$c, write_error=NULL, file_size=$s, file_mtime=$m WHERE path=$p",
                ("$c", writtenCaption), ("$s", size), ("$m", mtime), ("$p", path));
            cmd.ExecuteNonQuery();
        }
    }

    public void MarkWriteError(string path, string error)
    {
        lock (_lock)
        {
            using var cmd = Cmd("UPDATE photos SET write_error=$e WHERE path=$p", ("$e", error), ("$p", path));
            cmd.ExecuteNonQuery();
        }
    }

    public List<PhotoRecord> GetPendingWrites()
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT * FROM photos WHERE imported=1 AND is_draft=0 AND caption <> IFNULL(file_caption,'')");
            using var r = cmd.ExecuteReader();
            var list = new List<PhotoRecord>();
            while (r.Read()) list.Add(Read(r));
            return list;
        }
    }

    public List<PhotoRecord> GetMarkedForDelete()
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT * FROM photos WHERE marked_delete=1 ORDER BY path");
            using var r = cmd.ExecuteReader();
            var list = new List<PhotoRecord>();
            while (r.Read()) list.Add(Read(r));
            return list;
        }
    }

    /// <summary>Odstraní záznam fotky (po přesunu do koše). Historie zůstává kvůli případné obnově.</summary>
    public void RemovePhoto(string path)
    {
        lock (_lock)
        {
            using var cmd = Cmd("DELETE FROM photos WHERE path=$p", ("$p", path));
            cmd.ExecuteNonQuery();
        }
    }

    public List<HistoryEntry> GetHistory(string path, int limit = 50)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT id, caption, is_draft, created_at, source FROM caption_history WHERE path=$p ORDER BY id DESC LIMIT $l",
                ("$p", path), ("$l", limit));
            using var r = cmd.ExecuteReader();
            var list = new List<HistoryEntry>();
            while (r.Read()) list.Add(new(r.GetInt64(0), r.GetString(1), r.GetInt64(2) == 1, r.GetString(3), r.GetString(4)));
            return list;
        }
    }

    private void AddHistoryLocked(string path, string caption, bool isDraft, string source, bool merge)
    {
        var now = DateTime.UtcNow;
        if (merge)
        {
            long? mergeId = null;
            using (var last = Cmd("SELECT id, created_at, source FROM caption_history WHERE path=$p ORDER BY id DESC LIMIT 1", ("$p", path)))
            using (var r = last.ExecuteReader())
            {
                if (r.Read() && r.GetString(2) == source
                    && DateTime.TryParse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                    && now - at.ToUniversalTime() < HistoryMergeWindow)
                    mergeId = r.GetInt64(0);
            }
            if (mergeId is { } id)
            {
                using var upd = Cmd("UPDATE caption_history SET caption=$c, is_draft=$d WHERE id=$id",
                    ("$c", caption), ("$d", isDraft ? 1 : 0), ("$id", id));
                upd.ExecuteNonQuery();
                return;
            }
        }
        using var ins = Cmd("INSERT INTO caption_history(path, caption, is_draft, source, created_at) VALUES($p,$c,$d,$s,$t)",
            ("$p", path), ("$c", caption), ("$d", isDraft ? 1 : 0), ("$s", source), ("$t", now.ToString("O")));
        ins.ExecuteNonQuery();
    }

    // ---------- složky a stav aplikace ----------

    public (string? Kind, string? LastPhoto) GetFolder(string path)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT kind, last_photo FROM folders WHERE path=$p", ("$p", path));
            using var r = cmd.ExecuteReader();
            return r.Read() ? (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)) : (null, null);
        }
    }

    public Dictionary<string, string> GetFolderKinds()
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT path, kind FROM folders WHERE kind IS NOT NULL");
            using var r = cmd.ExecuteReader();
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (r.Read()) d[r.GetString(0)] = r.GetString(1);
            return d;
        }
    }

    public void SetFolderKind(string path, string? kind)
    {
        lock (_lock)
        {
            using var cmd = Cmd("INSERT INTO folders(path, kind) VALUES($p,$k) ON CONFLICT(path) DO UPDATE SET kind=$k",
                ("$p", path), ("$k", (object?)kind ?? DBNull.Value));
            cmd.ExecuteNonQuery();
        }
    }

    public Dictionary<string, string> GetAlbumIntros()
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT path, intro FROM album_intros");
            using var reader = cmd.ExecuteReader();
            var intros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) intros[reader.GetString(0)] = reader.GetString(1);
            return intros;
        }
    }

    public void SetAlbumIntro(string path, string intro)
    {
        lock (_lock)
        {
            using var cmd = Cmd("INSERT INTO album_intros(path, intro) VALUES($p,$i) ON CONFLICT(path) DO UPDATE SET intro=$i",
                ("$p", path), ("$i", intro));
            cmd.ExecuteNonQuery();
        }
    }

    public void SetLastPhoto(string folder, string photo)
    {
        lock (_lock)
        {
            using var cmd = Cmd("INSERT INTO folders(path, last_photo) VALUES($p,$l) ON CONFLICT(path) DO UPDATE SET last_photo=$l",
                ("$p", folder), ("$l", photo));
            cmd.ExecuteNonQuery();
            SetStateLocked("last_folder", folder);
        }
    }

    public string? GetState(string key)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT value FROM app_state WHERE key=$k", ("$k", key));
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetState(string key, string? value) { lock (_lock) SetStateLocked(key, value); }

    private void SetStateLocked(string key, string? value)
    {
        using var cmd = Cmd("INSERT INTO app_state(key, value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v",
            ("$k", key), ("$v", (object?)value ?? DBNull.Value));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Konzistentní kopie databáze (bezpečná i za běhu) – pro zálohu do synchronizované složky.</summary>
    public void BackupTo(string destinationFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        var tmp = destinationFile + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        lock (_lock)
        {
            using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString());
            dest.Open();
            _db.BackupDatabase(dest);
        }
        File.Move(tmp, destinationFile, overwrite: true);
    }

    // ---------- pomocné ----------

    private void Upsert(PhotoRecord p)
    {
        using var cmd = Cmd("""
            INSERT INTO photos(path, caption, is_draft, raw_dictation, skip, marked_delete, rotation, everyday, date_taken,
                               file_size, file_mtime, imported, file_caption, write_error, updated_at, caption_source, title)
            VALUES($path,$caption,$draft,$raw,$skip,$del,$rot,$every,$date,$size,$mtime,$imp,$fcap,$err,$now,$src,$title)
            ON CONFLICT(path) DO UPDATE SET caption=$caption, is_draft=$draft, raw_dictation=$raw, skip=$skip,
              marked_delete=$del, rotation=$rot, everyday=$every, date_taken=$date, file_size=$size, file_mtime=$mtime,
              imported=$imp, file_caption=$fcap, write_error=$err, updated_at=$now, caption_source=$src, title=$title
            """,
            ("$path", p.Path), ("$caption", p.Caption), ("$draft", p.IsDraft ? 1 : 0), ("$raw", p.RawDictation),
            ("$skip", p.Skip ? 1 : 0), ("$del", p.MarkedDelete ? 1 : 0), ("$rot", p.Rotation),
            ("$every", p.Everyday is null ? DBNull.Value : p.Everyday.Value ? 1 : 0),
            ("$date", (object?)p.DateTaken ?? DBNull.Value), ("$size", p.FileSize), ("$mtime", p.FileMtime),
            ("$imp", p.Imported ? 1 : 0), ("$fcap", (object?)p.FileCaption ?? DBNull.Value),
            ("$err", (object?)p.WriteError ?? DBNull.Value), ("$now", DateTime.UtcNow.ToString("O")),
            ("$src", p.CaptionSource), ("$title", (object?)p.Title ?? DBNull.Value));
        cmd.ExecuteNonQuery();
    }

    private static PhotoRecord Read(SqliteDataReader r) => new()
    {
        Path = r.GetString(r.GetOrdinal("path")),
        Caption = r.GetString(r.GetOrdinal("caption")),
        IsDraft = r.GetInt64(r.GetOrdinal("is_draft")) == 1,
        RawDictation = r.GetString(r.GetOrdinal("raw_dictation")),
        Skip = r.GetInt64(r.GetOrdinal("skip")) == 1,
        MarkedDelete = r.GetInt64(r.GetOrdinal("marked_delete")) == 1,
        Rotation = (int)r.GetInt64(r.GetOrdinal("rotation")),
        Everyday = r.IsDBNull(r.GetOrdinal("everyday")) ? null : r.GetInt64(r.GetOrdinal("everyday")) == 1,
        DateTaken = r.IsDBNull(r.GetOrdinal("date_taken")) ? null : r.GetString(r.GetOrdinal("date_taken")),
        FileSize = r.GetInt64(r.GetOrdinal("file_size")),
        FileMtime = r.GetInt64(r.GetOrdinal("file_mtime")),
        Imported = r.GetInt64(r.GetOrdinal("imported")) == 1,
        FileCaption = r.IsDBNull(r.GetOrdinal("file_caption")) ? null : r.GetString(r.GetOrdinal("file_caption")),
        WriteError = r.IsDBNull(r.GetOrdinal("write_error")) ? null : r.GetString(r.GetOrdinal("write_error")),
        CaptionSource = r.GetString(r.GetOrdinal("caption_source")),
        Title = r.IsDBNull(r.GetOrdinal("title")) ? null : r.GetString(r.GetOrdinal("title")),
    };

    private SqliteCommand Cmd(string sql, params (string Name, object Value)[] ps)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }

    private void Exec(string sql) { using var c = Cmd(sql); c.ExecuteNonQuery(); }
    private object? Scalar(string sql) { using var c = Cmd(sql); return c.ExecuteScalar(); }

    public void Dispose()
    {
        lock (_lock)
        {
            Exec("PRAGMA wal_checkpoint(TRUNCATE)");
            _db.Dispose();
        }
    }
}
