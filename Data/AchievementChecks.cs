using Cardex.Services;
using Microsoft.EntityFrameworkCore;

namespace CardexMobile.Data;

// Logique de condition pour chaque succès — le service lui-même (CardexCore) ne fait que la
// primitive d'unlock idempotente ; ce fichier connaît le contexte métier mobile (requêtes DB) et
// appelle AchievementService.CheckAsync quand une condition est remplie, comme MainViewModel
// côté desktop. Appelé depuis les sites d'action correspondants (SetDetailPageViewModel,
// TagsPageViewModel).
public static class AchievementChecks
{
    private static readonly (string Id, string[] Names)[] Teams =
    [
        ("regis",            ["Regice", "Registeel", "Regirock", "Regigigas", "Regieleki", "Regidrago"]),
        ("cynthia_wannabe",  ["Spiritomb", "Roselia", "Gastrodon", "Lucario", "Milotic", "Garchomp"]),
        ("blue_team",        ["Pidgeot", "Arcanine", "Rhydon", "Exeggutor", "Alakazam", "Blastoise", "Charizard", "Venusaur"]),
        ("red_team",         ["Blastoise", "Charizard", "Venusaur", "Snorlax", "Pikachu", "Espeon"]),
        ("steven_team",      ["Skarmory", "Aggron", "Cradily", "Armaldo", "Claydol", "Metagross"]),
        ("n_team",           ["Zekrom", "Reshiram", "Zoroark", "Archeops", "Klinklang", "Carracosta", "Vanilluxe"]),
        ("diantha_team",     ["Hawlucha", "Tyrantrum", "Abomasnow", "Gourgeist", "Goodra", "Gardevoir"]),
        ("kukui_team",       ["Lycanroc", "Ninetales", "Braviary", "Magnezone", "Snorlax", "Incineroar", "Primarina", "Decidueye"]),
        ("leon_team",        ["Aegislash", "Haxorus", "Dragapult", "Charizard", "Inteleon", "Mr. Rime", "Cinderace", "Seismitoad", "Rillaboom", "Rhyperior"]),
        ("geeta_team",       ["Espathra", "Avalugg", "Gogoat", "Kingambit", "Veluza", "Glimmora"]),
    ];

    public static async Task CheckSetCompletionAsync(MobileAppDbContext db, string setId, string setName, string series, int total)
    {
        await AchievementService.CheckAsync("first_set", db);

        if (setName.Contains("McDonald", StringComparison.OrdinalIgnoreCase)) await AchievementService.CheckAsync("mcdonalds", db);
        if (setId == "pop5") await AchievementService.CheckAsync("pop5", db);
        if (series is "Base" or "Gym") await AchievementService.CheckAsync("genwunner", db);
        if (series is "Neo" or "E-Card") await AchievementService.CheckAsync("golden_gen", db);
        if (series == "EX") await AchievementService.CheckAsync("too_much_water", db);
        if (series is "Diamond & Pearl" or "Platinum" or "HeartGold & SoulSilver") await AchievementService.CheckAsync("gen4_win", db);
        if (series == "Black & White") await AchievementService.CheckAsync("unova_enthusiast", db);
        if (series == "XY") await AchievementService.CheckAsync("all_3d_now", db);
        if (series == "Sun & Moon") await AchievementService.CheckAsync("tropical_trip", db);
        if (series == "Sword & Shield") await AchievementService.CheckAsync("collector_bruv", db);
        if (series == "Scarlet & Violet") await AchievementService.CheckAsync("sandwich_bikes", db);
        if (total > 250) await AchievementService.CheckAsync("big_set", db);

        // Triple threat — recompte global des sets complets (possédé non exclu >= total non exclu).
        var ownedBySet = await db.OwnedCards.Where(o => o.Quantity > 0)
            .Join(db.CachedCards, o => o.CardId, c => c.CardId, (o, c) => c.SetId)
            .GroupBy(id => id)
            .Select(g => new { SetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SetId, x => x.Count);
        var excludedBySet = await db.ExcludedCards
            .Join(db.CachedCards, e => e.CardId, c => c.CardId, (e, c) => c.SetId)
            .GroupBy(id => id)
            .Select(g => new { SetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SetId, x => x.Count);
        var allSets = await db.CachedSets.ToListAsync();
        var completeCount = allSets.Count(s =>
        {
            var effectiveTotal = s.Total - excludedBySet.GetValueOrDefault(s.SetId);
            return effectiveTotal > 0 && ownedBySet.GetValueOrDefault(s.SetId) >= effectiveTotal;
        });
        if (completeCount >= 3) await AchievementService.CheckAsync("triple_threat", db);
    }

    public static async Task CheckOwnershipAsync(MobileAppDbContext db, int cardQuantity)
    {
        if (cardQuantity >= 5)  await AchievementService.CheckAsync("duplicate_5", db);
        if (cardQuantity >= 20) await AchievementService.CheckAsync("duplicate_20", db);

        var totalOwned = await db.OwnedCards.SumAsync(o => (int?)o.Quantity) ?? 0;
        if (totalOwned >= 1000) await AchievementService.CheckAsync("collector_1000", db);
        if (totalOwned >= 2500) await AchievementService.CheckAsync("hoarder_2500", db);

        if (await CountDistinctOwnedByRarityAsync(db, "Rare Holo Star") >= 5) await AchievementService.CheckAsync("rare_holo_star_5", db);
        if (await CountDistinctOwnedByNameAsync(db, "Darmanitan") >= 5)      await AchievementService.CheckAsync("darmanitan_5", db);
        if (await CountDistinctOwnedByNameAsync(db, "Garchomp") >= 8)        await AchievementService.CheckAsync("garchomp_8", db);
        if (await CountDistinctOwnedByAnyNameAsync(db, "Wimpod", "Golisopod") >= 6) await AchievementService.CheckAsync("wimpod_6", db);

        foreach (var (id, names) in Teams)
            if (await OwnsAtLeastOneOfEachAsync(db, names))
                await AchievementService.CheckAsync(id, db);
    }

    public static async Task CheckWantedAsync(MobileAppDbContext db)
    {
        var count = await db.WantedCards.CountAsync();
        if (count >= 10) await AchievementService.CheckAsync("wanted_10", db);
    }

    public static async Task CheckExcludedAsync(MobileAppDbContext db)
    {
        var count = await db.ExcludedCards.CountAsync();
        if (count >= 10) await AchievementService.CheckAsync("excluded_10", db);
    }

    private static async Task<int> CountDistinctOwnedByRarityAsync(MobileAppDbContext db, string rarity)
    {
        var ownedIds = await db.OwnedCards.Where(o => o.Quantity > 0).Select(o => o.CardId).ToListAsync();
        return await db.CachedCards.CountAsync(c => ownedIds.Contains(c.CardId) && c.Rarity == rarity);
    }

    private static async Task<int> CountDistinctOwnedByNameAsync(MobileAppDbContext db, string nameContains)
    {
        var ownedIds = await db.OwnedCards.Where(o => o.Quantity > 0).Select(o => o.CardId).ToListAsync();
        return await db.CachedCards.CountAsync(c => ownedIds.Contains(c.CardId) && EF.Functions.Like(c.Name, $"%{nameContains}%"));
    }

    private static async Task<int> CountDistinctOwnedByAnyNameAsync(MobileAppDbContext db, params string[] names)
    {
        var ownedIds = await db.OwnedCards.Where(o => o.Quantity > 0).Select(o => o.CardId).ToListAsync();
        return await db.CachedCards.CountAsync(c => ownedIds.Contains(c.CardId) && names.Any(n => EF.Functions.Like(c.Name, $"%{n}%")));
    }

    private static async Task<bool> OwnsAtLeastOneOfEachAsync(MobileAppDbContext db, string[] names)
    {
        foreach (var name in names)
        {
            var cardIds = await db.CachedCards.Where(c => EF.Functions.Like(c.Name, $"%{name}%")).Select(c => c.CardId).ToListAsync();
            if (cardIds.Count == 0) return false;
            var hasOwned = await db.OwnedCards.AnyAsync(o => o.Quantity > 0 && cardIds.Contains(o.CardId));
            if (!hasOwned) return false;
        }
        return true;
    }
}
