using System.Text.Json;

namespace Fotoalbum.Core;

/// <summary>Nastavení aplikace. Ukládá se do profilu uživatele, nikdy do repozitáře.</summary>
public sealed class AppSettings
{
    /// <summary>Kořenová složka s fotkami (např. D:\Fotky).</summary>
    public string? PhotoRoot { get; set; }

    /// <summary>Volitelná cesta k exiftool(.exe). Prázdné = hledá se vedle aplikace a v PATH.</summary>
    public string? ExifToolPath { get; set; }

    /// <summary>Kolik posledních záloh databáze ponechat ve složce „Fotoalbum – databáze (nemazat)/zaloha“.</summary>
    public int BackupsToKeep { get; set; } = 10;

    /// <summary>Po připojení karty z fotoaparátu automaticky zkopírovat nové fotky (Fotoalbum běží na pozadí u hodin).</summary>
    public bool AutoImport { get; set; } = true;

    /// <summary>Kanál aktualizací: „stable“ (výchozí, schválené verze) nebo „test“ (jen pro správce).</summary>
    public string UpdateChannel { get; set; } = "stable";

    /// <summary>Model OpenRouteru pro korekturu (prázdné = výchozí Claude Haiku 5.5).</summary>
    public string? AiTextModel { get; set; }

    /// <summary>Model OpenRouteru pro přepis řeči (prázdné = první vhodný dostupný).</summary>
    public string? AiAudioModel { get; set; }

    /// <summary>Slovníček jmen a míst pro přepis i korekturu (např. „Jarka, Ngorongoro, Masai Mara“).</summary>
    public string? Glossary { get; set; }

    /// <summary>Délka popisku: kratky / stredni / delsi.</summary>
    public string CaptionLength { get; set; } = "stredni";

    /// <summary>Kolik zatím stála AI (USD, podle OpenRouteru).</summary>
    public double AiCostUsd { get; set; }
}

/// <summary>Umístění dat aplikace mimo složku s fotkami.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataDir = null)
    {
        DataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            "Fotoalbum");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ThumbDir);
    }

    public string DataDir { get; }
    public string SettingsFile => Path.Combine(DataDir, "settings.json");
    public string CatalogFile => Path.Combine(DataDir, "katalog.db");
    public string ThumbDir => Path.Combine(DataDir, "nahledy");
    /// <summary>Evidence fotek už jednou zkopírovaných z karty (aby se smazané fotky nevracely).</summary>
    public string ImportIndexFile => Path.Combine(DataDir, "import.db");
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private AppSettings _current;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
        _current = Load();
    }

    public AppSettings Current { get { lock (_lock) return _current; } }

    public event Action? Changed;

    public void Update(Action<AppSettings> change)
    {
        lock (_lock)
        {
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_current))!;
            change(copy);
            var tmp = _paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy, Json));
            File.Move(tmp, _paths.SettingsFile, overwrite: true);
            _current = copy;
        }
        Changed?.Invoke();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile)) ?? new();
        }
        catch (JsonException)
        {
            // Poškozený soubor: začneme s výchozím nastavením, starý soubor ponecháme pro kontrolu.
            File.Copy(_paths.SettingsFile, _paths.SettingsFile + ".poskozeny", overwrite: true);
        }
        return new AppSettings();
    }
}
