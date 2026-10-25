using CardexMobile.Services;
using CommunityToolkit.Maui.Alerts;
using Microsoft.EntityFrameworkCore;

namespace CardexMobile.Data;

// EnsureCreated() + migrations légères + seed TCGdex doivent rester hors du thread principal au
// démarrage : les exécuter de façon synchrone dans MauiProgram.CreateMauiApp() a provoqué un ANR
// (Application Not Responding, process tué après ~10s) car l'UI n'a pas la main pour afficher sa
// première frame pendant ce temps. Ce singleton démarre le travail en tâche de fond dès sa
// construction ; les ViewModels attendent `ReadyTask` avant de toucher leur propre DbContext.
public class DbInitializer
{
    public Task ReadyTask { get; }

    public DbInitializer(IServiceProvider services)
    {
        ReadyTask = Task.Run(() => InitializeAsync(services));
    }

    private static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MobileAppDbContext>();

        await db.Database.EnsureCreatedAsync();
        MigrateSchema(db);
        await TcgdexSeeder.SeedIfNeededAsync(db);
        await BackfillSupertypesAsync(db);

        await CheckForUpdateAsync(scope.ServiceProvider.GetRequiredService<MobileUpdateService>());

        // Fire-and-forget, volontairement après ReadyTask (voir TriggerOnce) : rattrape en tâche de
        // fond les sets jamais visités/possédés, sinon invisibles de Search (voir CatalogPreloader).
        scope.ServiceProvider.GetRequiredService<CatalogPreloader>().TriggerOnce();
    }

    // Port de App.xaml.cs.BackfillSupertypesAsync (desktop) : évite de refaire un fetch réseau par
    // carte juste pour récupérer Supertype — même fichier de seed embarqué (CardId -> code compact
    // T/E/vide), copié tel quel dans Resources/Raw. Nécessaire pour que le filtre "Supertype ==
    // Pokémon" de la stat "le plus collecté" (voir CollectionPageViewModel.LoadStatsAsync) ne
    // retombe pas à zéro pour tout le catalogue déjà en cache avant ce correctif.
    private static async Task BackfillSupertypesAsync(MobileAppDbContext db)
    {
        try
        {
            if (!await db.CachedCards.AnyAsync(c => c.Supertype == null)) return;

            using var stream = await FileSystem.OpenAppPackageFileAsync("supertypes.json");
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync();
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null || map.Count == 0) return;

            static string Expand(string code) => code switch
            {
                "T" => "Trainer",
                "E" => "Energy",
                _   => "Pokémon"
            };

            var nullIds = await db.CachedCards
                .Where(c => c.Supertype == null)
                .Select(c => c.CardId)
                .ToListAsync();

            var toUpdate = nullIds
                .Where(id => map.ContainsKey(id))
                .Select(id => (Id: id, Supertype: Expand(map[id])))
                .ToList();

            if (toUpdate.Count == 0) return;

            // AutoDetectChangesEnabled=false (optimisation desktop) empêcherait SaveChangesAsync de
            // détecter la mutation de Supertype faite via un simple setter ci-dessous — le batch
            // tournerait sans erreur mais sans jamais rien persister. Détection par défaut gardée.
            foreach (var batch in toUpdate.Chunk(500))
            {
                var ids = batch.Select(x => x.Id).ToList();
                var cards = await db.CachedCards.Where(c => ids.Contains(c.CardId)).ToListAsync();
                foreach (var card in cards)
                    card.Supertype = batch.First(x => x.Id == card.CardId).Supertype;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"BackfillSupertypesAsync failed: {ex}"); }
    }

    // Simple notification au démarrage (pas de bannière globale comme sur desktop — pas de slot
    // naturel pour ça au-dessus de la navigation Shell). L'installation elle-même se fait depuis
    // Settings, qui refait son propre check pour afficher le bouton "Install update".
    private static async Task CheckForUpdateAsync(MobileUpdateService updateService)
    {
        var info = await updateService.CheckAsync();
        if (info is null) return;

        MainThread.BeginInvokeOnMainThread(() =>
            _ = Toast.Make($"📦 Update {info.Version} available — install it from Settings.").Show());
    }

    // La détection d'une synchro en attente (et sa confirmation par l'utilisateur) vit désormais
    // dans SetsPage.xaml.cs — ça nécessite Shell.Current (page affichée), indisponible ici qui
    // tourne en tâche de fond avant même que l'UI soit construite. Voir incident 2026-10-15.

    // EnsureCreated() ne crée le schéma qu'une seule fois et ne le fait jamais évoluer ensuite —
    // toute base déjà présente sur l'appareil (install précédente) doit recevoir ces colonnes à la
    // main, sous peine de SqliteException "no such column" dès la première requête. Même principe
    // que les migrations ALTER TABLE du desktop (App.xaml.cs).
    private static void MigrateSchema(MobileAppDbContext db)
    {
        void TryAlter(string sql)
        {
            try { db.Database.ExecuteSqlRaw(sql); } catch { }
        }

        TryAlter("ALTER TABLE CachedSets ADD COLUMN PtcgoCode TEXT");
        TryAlter("ALTER TABLE CachedSets ADD COLUMN ShortCode TEXT");
        TryAlter("ALTER TABLE CachedSets ADD COLUMN StandardLegal INTEGER NOT NULL DEFAULT 0");
        TryAlter("ALTER TABLE CachedSets ADD COLUMN ExpandedLegal INTEGER NOT NULL DEFAULT 0");
        TryAlter("ALTER TABLE CachedSets ADD COLUMN Source TEXT NOT NULL DEFAULT 'pokemontcgio'");
        TryAlter("ALTER TABLE CachedCards ADD COLUMN Supertype TEXT");
        TryAlter("ALTER TABLE CachedCards ADD COLUMN Subtypes TEXT");
        TryAlter("ALTER TABLE CachedCards ADD COLUMN Types TEXT");
        TryAlter("ALTER TABLE CachedCards ADD COLUMN Source TEXT NOT NULL DEFAULT 'pokemontcgio'");
        TryAlter("ALTER TABLE OwnedCards ADD COLUMN IsHolo INTEGER NOT NULL DEFAULT 0");
        TryAlter(@"
            CREATE TABLE IF NOT EXISTS Tags (
                Id    INTEGER PRIMARY KEY AUTOINCREMENT,
                Name  TEXT NOT NULL,
                Color TEXT NOT NULL DEFAULT '#8888aa')");
        TryAlter(@"
            CREATE TABLE IF NOT EXISTS CardTags (
                CardId TEXT NOT NULL PRIMARY KEY,
                TagId  INTEGER NOT NULL)");
        TryAlter(@"
            CREATE TABLE IF NOT EXISTS UnlockedAchievements (
                Id         TEXT NOT NULL PRIMARY KEY,
                UnlockedAt TEXT NOT NULL)");
        TryAlter(@"
            CREATE TABLE IF NOT EXISTS OwnedCardLanguages (
                CardId   TEXT NOT NULL,
                SetId    TEXT NOT NULL DEFAULT '',
                Language TEXT NOT NULL DEFAULT 'EN',
                Quantity INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (CardId, Language))");
    }
}
