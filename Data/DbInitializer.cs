using Cardex.Services;
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

        var settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        await AutoPullSyncIfNeededAsync(db, settings);
    }

    // Rapatriement automatique au démarrage : si la dernière synchro connue côté serveur vient du
    // DESKTOP et est plus récente que notre dernier sync connu, on l'applique directement sans
    // demander — comportement demandé ("si on détecte que la dernière synchro vient de l'autre, on
    // récupère automatiquement"). Fait partie de ReadyTask : les pages interrogent la DB à chaque
    // affichage après avoir attendu ReadyTask, donc pas besoin de rafraîchir leur UI à la main ici.
    // Best-effort : une erreur réseau ne doit jamais empêcher le reste de l'app de démarrer
    // (timeout HTTP court dans CollectionSyncService pour ne pas allonger ReadyTask indéfiniment).
    private static async Task AutoPullSyncIfNeededAsync(MobileAppDbContext db, AppSettings settings)
    {
        var apiKey = settings.CollectionSyncApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        try
        {
            var syncService = new CollectionSyncService(db, apiKey);
            var remote = await syncService.FetchRemoteStateAsync();
            if (remote is null || remote.Origin == "mobile") return;

            var remoteUpdatedAt = DateTime.Parse(remote.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (settings.LastCollectionSyncAt is { } lastKnown && remoteUpdatedAt <= lastKnown) return;

            await syncService.ApplyCollectionJsonAsync(remote.CollectionJson);
            settings.LastCollectionSyncAt = remoteUpdatedAt;
            settings.LastCollectionSyncOrigin = remote.Origin;
            settings.Save();
        }
        catch { /* best-effort, ne doit jamais bloquer le démarrage */ }
    }

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
    }
}
