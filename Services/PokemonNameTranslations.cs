using System.Text.Json;

namespace CardexMobile.Services;

// Table EN -> noms étrangers (FR/DE/ES/IT/JA/KO/ZH, alignés sur LanguageCatalog.All), générée hors
// ligne depuis les CSV publics PokeAPI (pokemon_species_names.csv) et embarquée dans
// Resources/Raw/pokemon_translations.json — pas d'appel réseau au moment du scan. Sert au scan OCR
// (ScanTestPageViewModel) : une carte étrangère (ex. "Tyranocif") ne matchera jamais le nom
// catalogue anglais ("Tyranitar") par simple Contains, d'où ce lookup inverse.
public static class PokemonNameTranslations
{
    private static Dictionary<string, string[]>? _map;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static async Task<IReadOnlyDictionary<string, string[]>> LoadAsync()
    {
        if (_map is not null) return _map;
        await Lock.WaitAsync();
        try
        {
            if (_map is not null) return _map;
            using var stream = await FileSystem.OpenAppPackageFileAsync("pokemon_translations.json");
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync();
            _map = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json) ?? new();
        }
        finally
        {
            Lock.Release();
        }
        return _map;
    }
}
