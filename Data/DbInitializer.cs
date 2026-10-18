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

        await CheckForUpdateAsync(scope.ServiceProvider.GetRequiredService<MobileUpdateService>());
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
