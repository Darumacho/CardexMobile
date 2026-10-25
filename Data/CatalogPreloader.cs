using Cardex.Models;
using Cardex.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardexMobile.Data;

// Équivalent mobile de MainViewModel.PreloadAllCardsAsync côté desktop. Sans ça, CachedCards ne
// contient que les sets déjà visités (SetDetailPage) ou possédés (EnsureCachedCardsForOwnedSetsAsync
// côté Collection) — un set jamais ouvert et non possédé reste invisible de Search, qui ne
// cherche que dans le cache local (voir SearchPageViewModel.RunSearchAsync). Tourne une seule fois
// par lancement d'app, déclenché depuis DbInitializer ; concurrence limitée à 3 fetches simultanés
// comme sur desktop, écriture séquentielle pour éviter les conflits EF Core entre tâches.
public class CatalogPreloader
{
    private readonly IServiceProvider _services;
    private bool _started;

    public CatalogPreloader(IServiceProvider services) => _services = services;

    public void TriggerOnce()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MobileAppDbContext>();
            var tcgService = scope.ServiceProvider.GetRequiredService<PokemonTcgService>();

            var allSets = await tcgService.GetSetsAsync();
            var cachedSetIds = (await db.CachedCards.Select(c => c.SetId).Distinct().ToListAsync()).ToHashSet();
            var toLoad = allSets.Select(s => s.Id).Where(id => !cachedSetIds.Contains(id)).ToList();
            if (toLoad.Count == 0) return;

            using var sem = new SemaphoreSlim(3, 3);

            async Task<(string SetId, List<ApiCard>? Cards)> FetchAsync(string setId)
            {
                await sem.WaitAsync();
                try { return (setId, await tcgService.GetCardsAsync(setId)); }
                catch { return (setId, null); }
                finally { sem.Release(); }
            }

            var fetchTasks = toLoad.Select(FetchAsync).ToList();

            foreach (var task in fetchTasks)
            {
                var (setId, cards) = await task;
                if (cards is null || cards.Count == 0) continue;
                // Un autre chemin (EnsureCachedCardsForOwnedSetsAsync, visite manuelle du set
                // pendant que ce préchargement tournait) a pu déjà remplir ce set entre-temps.
                if (await db.CachedCards.AnyAsync(c => c.SetId == setId)) continue;

                var order = 0;
                db.CachedCards.AddRange(cards.Select(c => new CachedCard
                {
                    CardId = c.Id, SetId = setId, Name = c.Name, Number = c.Number,
                    ImageSmall = c.Images.Small, ImageLarge = c.Images.Large,
                    Rarity = c.Rarity, SortOrder = order++, Supertype = c.Supertype,
                    CmLow  = c.Cardmarket?.Prices?.LowPrice,
                    TcgLow = c.Tcgplayer?.Prices?.Values.FirstOrDefault()?.Low,
                    CmUrl  = c.Cardmarket?.Url,
                    TcgUrl = c.Tcgplayer?.Url
                }));

                try { await db.SaveChangesAsync(); }
                catch { db.ChangeTracker.Clear(); /* best-effort, retenté au prochain lancement */ }
            }
        }
        catch { /* best-effort : pas de réseau au démarrage, retenté au prochain lancement */ }
    }
}
