using System.Collections.Concurrent;
using Cardex.Services;

namespace CardexMobile.Services;

// Icônes Pokémon embarquées localement (Resources/Raw/pokesprites/{slug}.png, ~1025 espèces
// récupérées hors-ligne depuis PokeAPI — voir mémoire [[project_ocr_scan]]), pour l'affichage dans
// les résultats de Card Scan sans appel réseau. Contrairement à PokeSpriteService (utilisé pour
// l'équipe de favoris côté Stats/Collection), qui interroge PokeAPI en direct — inadapté à la boucle
// de scan live, répétée toutes les 1.5s.
public static class LocalPokemonSpriteCache
{
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new();

    public static async Task<ImageSource?> GetSpriteAsync(string cardName)
    {
        var slug = PokeSpriteService.GetLocalSpriteSlug(cardName);
        if (string.IsNullOrEmpty(slug)) return null;

        if (!Cache.TryGetValue(slug, out var bytes))
        {
            bytes = await TryLoadAsync(slug);
            Cache[slug] = bytes;
        }

        return bytes is null ? null : ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private static async Task<byte[]?> TryLoadAsync(string slug)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync($"pokesprites/{slug}.png");
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
        catch
        {
            // Espèce non couverte (Trainer/Energy, forme régionale, nom mal reconnu...) — best-effort,
            // l'UI masque simplement l'icône (voir CardListItem.HasSprite).
            return null;
        }
    }
}
