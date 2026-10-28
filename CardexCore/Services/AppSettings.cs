using System.IO;
using System.Text.Json;

namespace Cardex.Services;

public class AppSettings
{
    public string? ApiKey { get; set; }
    public string CollectionBorderColor { get; set; } = "#3a7fc1";
    public bool ShowMyCollection { get; set; } = true;
    public bool ShowStats { get; set; } = true;
    public bool StatsTeamMode { get; set; } = false;

    // Clé API Cardex fournie par l'utilisateur pour la synchro de collection (distincte de la clé
    // catalogue PokemonTcgService ci-dessus) — bookkeeping purement local, jamais envoyé dans le
    // JSON synchronisé.
    public string? CollectionSyncApiKey { get; set; }
    public DateTime? LastCollectionSyncAt { get; set; }
    public string? LastCollectionSyncOrigin { get; set; }

    // Langues suivies pour le décompte de collection (voir LanguageCatalog). Anglais toujours
    // présent par défaut ; au-delà d'une langue, le compteur d'exemplaires d'une carte devient un
    // select par langue plutôt qu'un simple +/-. Même réglage que côté desktop (settings.json
    // séparés, pas partagés).
    public List<string> TrackedLanguages { get; set; } = new() { "EN" };

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cardex", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), _json) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, _json));
        }
        catch { }
    }
}
