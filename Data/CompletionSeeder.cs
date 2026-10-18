using System.Text.Json;
using Cardex.Models;
using Microsoft.EntityFrameworkCore;

namespace CardexMobile.Data;

// Certains sets pokemontcg.io ont une pagination cassée côté API (ex: Ascended Heroes/me2pt5,
// 295 cartes annoncées mais `orderBy=number` échoue au-delà — voire dès la première page selon
// les jours — et l'app ne récupère que 250 cartes, dans un ordre non fiable). Même bug déjà
// rencontré et corrigé côté desktop (SeedCompletionCardsAsync, App.xaml.cs) : plutôt que de
// dépendre du fetch live pour ces sets connus, on charge directement depuis ce seed statique
// (généré une fois depuis TCGdex, même fichier que le desktop), complet et correctement ordonné.
public static class CompletionSeeder
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private record CompletionCardEntry(string Id, string Name, string Number, string SetId,
        string ImageSmall, string? ImageLarge, string? Rarity, string? Supertype, string? Subtypes, string? Types);

    private static List<CompletionCardEntry>? _cache;

    private static async Task<List<CompletionCardEntry>> LoadAllAsync()
    {
        if (_cache is not null) return _cache;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("tcgdex_completion_cards.json");
            using var reader = new StreamReader(stream);
            _cache = JsonSerializer.Deserialize<List<CompletionCardEntry>>(await reader.ReadToEndAsync(), JsonOpts) ?? [];
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }

    // Retourne null si ce set n'est pas couvert par le seed de secours (cas normal, immense
    // majorité des sets) — l'appelant garde alors son comportement de fetch live habituel.
    private static async Task<List<CachedCard>?> GetCardsForSetAsync(string setId)
    {
        var all = await LoadAllAsync();
        // Le fichier source n'est PAS trié par numéro (généré dans l'ordre de scraping TCGdex,
        // voir ex. "2,4,1,3,5..." pour me2pt5) — trier explicitement, sans quoi le même problème
        // de désordre que celui qu'on corrige reviendrait par un autre chemin.
        var matching = all.Where(c => c.SetId == setId)
            .OrderBy(c => int.TryParse(c.Number, out var n) ? n : int.MaxValue)
            .ThenBy(c => c.Number)
            .ToList();
        if (matching.Count == 0) return null;

        var sort = 0;
        return matching.Select(c => new CachedCard
        {
            CardId = c.Id, SetId = c.SetId, Name = c.Name, Number = c.Number,
            ImageSmall = c.ImageSmall, ImageLarge = c.ImageLarge, Rarity = c.Rarity,
            SortOrder = sort++, Supertype = c.Supertype, Subtypes = c.Subtypes,
            Types = c.Types, Source = "tcgdex"
        }).ToList();
    }

    // Appelé depuis SetDetailPageViewModel.LoadAsync juste avant le fetch live habituel. Si ce set
    // est couvert par le seed ET que le cache local ne correspond pas déjà (vide, ou incomplet à
    // cause du bug), remplace entièrement les CachedCards de ce set par le jeu de secours. Les
    // CardId restent identiques à la convention pokemontcg.io ("{setId}-{number}"), donc les
    // éventuels OwnedCards/WantedCards/CardTags déjà enregistrés pour ce set restent valides.
    public static async Task<List<CachedCard>?> ReconcileAsync(MobileAppDbContext db, string setId, List<CachedCard> currentlyCached)
    {
        var fallback = await GetCardsForSetAsync(setId);
        if (fallback is null) return null;
        if (currentlyCached.Count == fallback.Count) return currentlyCached;

        await db.CachedCards.Where(c => c.SetId == setId).ExecuteDeleteAsync();
        // ExecuteDeleteAsync contourne le change tracker (DELETE direct en SQL) — les entités
        // qu'on vient de lire dans `currentlyCached` restent trackées alors que leurs lignes
        // n'existent plus en base ; sans ce Clear, l'AddRange suivant sur les mêmes CardId
        // lèverait un conflit de clé (même piège déjà rencontré avec la sync).
        db.ChangeTracker.Clear();
        db.CachedCards.AddRange(fallback);
        await db.SaveChangesAsync();
        return fallback;
    }
}
