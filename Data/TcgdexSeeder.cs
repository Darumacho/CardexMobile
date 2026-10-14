using System.Text.Json;
using Cardex.Models;
using Microsoft.EntityFrameworkCore;

namespace CardexMobile.Data;

// Secours catalogue minimal pour les sets absents de pokemontcg.io (ex: MEP Black Star Promos,
// Trainer Kits). Seed statique embarqué (Resources/Raw), add-only : contrairement au desktop, pas
// besoin de réconciliation update/remove, la base mobile démarre vide.
public static class TcgdexSeeder
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private record SeedSetEntry(string Id, string Name, string Series, int Total,
        string ReleaseDate, string LogoUrl, string SymbolUrl, bool StandardLegal, bool ExpandedLegal);
    private record SeedCardEntry(string Id, string Name, string Number, string SetId,
        string ImageSmall, string? ImageLarge, string? Rarity, string? Supertype, string? Subtypes, string? Types);

    public static async Task SeedIfNeededAsync(MobileAppDbContext db)
    {
        try
        {
            if (await db.CachedSets.AnyAsync(s => s.Source == "tcgdex")) return;

            using var setsStream = await FileSystem.OpenAppPackageFileAsync("tcgdex_sets.json");
            using var setsReader = new StreamReader(setsStream);
            var sets = JsonSerializer.Deserialize<List<SeedSetEntry>>(await setsReader.ReadToEndAsync(), JsonOpts);
            if (sets is null || sets.Count == 0) return;

            using var cardsStream = await FileSystem.OpenAppPackageFileAsync("tcgdex_cards.json");
            using var cardsReader = new StreamReader(cardsStream);
            var cards = JsonSerializer.Deserialize<List<SeedCardEntry>>(await cardsReader.ReadToEndAsync(), JsonOpts) ?? [];

            db.CachedSets.AddRange(sets.Select(s => new CachedSet
            {
                SetId = s.Id, Name = s.Name, Series = s.Series, Total = s.Total,
                ReleaseDate = s.ReleaseDate, LogoUrl = s.LogoUrl, SymbolUrl = s.SymbolUrl,
                CachedAt = DateTime.UtcNow, StandardLegal = s.StandardLegal, ExpandedLegal = s.ExpandedLegal,
                Source = "tcgdex"
            }));

            var sort = 0;
            db.CachedCards.AddRange(cards.Select(c => new CachedCard
            {
                CardId = c.Id, SetId = c.SetId, Name = c.Name, Number = c.Number,
                ImageSmall = c.ImageSmall, ImageLarge = c.ImageLarge, Rarity = c.Rarity,
                SortOrder = sort++, Supertype = c.Supertype, Subtypes = c.Subtypes,
                Types = c.Types, Source = "tcgdex"
            }));

            await db.SaveChangesAsync();
        }
        catch
        {
            // Best-effort : un seed de secours manquant/corrompu ne doit jamais empêcher l'app de démarrer.
        }
    }
}
