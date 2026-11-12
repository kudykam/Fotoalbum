namespace Fotoalbum.Server;

/// <summary>Stav aplikace a aktualizací pro záhlaví a skryté nastavení.</summary>
/// <param name="Status">none, checking, downloading, ready, uptodate, error, unavailable</param>
/// <param name="UpdatedTo">Verze, na kterou se program právě aktualizoval (ukáže se jednou).</param>
public sealed record AppUpdateInfo(string Version, string Channel, bool Installed, string Status,
    string? NewVersion, string? Message, string? UpdatedTo);

/// <summary>Ovládání aktualizací – poskytuje ho Windows aplikace (v prohlížeči není k dispozici).</summary>
public interface IAppUpdates
{
    AppUpdateInfo Info { get; }
    /// <summary>Zkontroluje a stáhne aktualizaci hned.</summary>
    Task CheckNowAsync();
    /// <summary>Přepne kanál (stable/test) a hned zkontroluje.</summary>
    Task SetChannelAsync(string channel);
}
