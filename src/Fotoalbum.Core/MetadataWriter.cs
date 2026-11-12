using System.Collections.Concurrent;

namespace Fotoalbum.Core;

/// <summary>
/// Zapisuje popisky do fotek na pozadí. Změny se nejdřív uloží do databáze (okamžitě),
/// do souboru se zapíšou chvíli po posledním psaní, při přechodu na jinou fotku nebo při zavření.
/// </summary>
public sealed class MetadataWriter : IDisposable
{
    private readonly CatalogDb _db;
    private readonly Func<PhotoLibrary?> _library;
    private readonly Func<ExifTool?> _exif;
    private readonly TimeSpan _delay;
    private readonly ConcurrentDictionary<string, DateTime> _due = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _timer;

    public MetadataWriter(CatalogDb db, Func<PhotoLibrary?> library, Func<ExifTool?> exif, TimeSpan? delay = null)
    {
        _db = db;
        _library = library;
        _exif = exif;
        _delay = delay ?? TimeSpan.FromSeconds(3);
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public int PendingCount => _due.Count;

    public void Schedule(string path) => _due[path] = DateTime.UtcNow + _delay;

    /// <summary>Zapíše jednu fotku hned (např. při přechodu na další fotku).</summary>
    public void WriteNow(string path)
    {
        _due.TryRemove(path, out _);
        WriteOne(path);
    }

    /// <summary>Zapíše vše, co čeká – volá se při zavření programu.</summary>
    public void FlushAll()
    {
        foreach (var p in _due.Keys.ToList()) { _due.TryRemove(p, out _); WriteOne(p); }
        foreach (var rec in _db.GetPendingWrites()) WriteOne(rec.Path);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        foreach (var (path, due) in _due)
        {
            if (due > now) continue;
            if (_due.TryRemove(new KeyValuePair<string, DateTime>(path, due))) WriteOne(path);
        }
    }

    private void WriteOne(string path)
    {
        var lib = _library();
        var exif = _exif();
        if (lib is null || exif is null) return;
        _writeLock.Wait();
        try
        {
            var rec = _db.GetPhoto(path);
            if (rec is null || !rec.NeedsFileWrite) return;
            var full = lib.Resolve(path);
            if (!File.Exists(full)) return;
            try
            {
                exif.WriteCaption(full, rec.Caption, rec.Title);
                var fi = new FileInfo(full);
                _db.MarkWritten(path, rec.Caption, fi.Length, fi.LastWriteTimeUtc.Ticks);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException)
            {
                _db.MarkWriteError(path, e.Message);
            }
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose() => _timer.Dispose();
}
