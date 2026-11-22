using CardexMobile.Data;
using CardexMobile.Pages;
using CardexMobile.Services;
using CardexMobile.ViewModels;
using Cardex.Services;
using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardexMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitCamera()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        AppSettings.FilePath = Path.Combine(FileSystem.AppDataDirectory, "settings.json");
        var settings = AppSettings.Load();

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(_ => new PokemonTcgService(settings.ApiKey));
        builder.Services.AddSingleton<MobileUpdateService>();
        builder.Services.AddDbContext<MobileAppDbContext>();

        builder.Services.AddTransient<SetsPageViewModel>();
        builder.Services.AddTransient<SetDetailPageViewModel>();
        builder.Services.AddTransient<SearchPageViewModel>();
        builder.Services.AddTransient<CollectionPageViewModel>();
        builder.Services.AddTransient<SettingsPageViewModel>();
        builder.Services.AddTransient<TagsPageViewModel>();
        builder.Services.AddTransient<AchievementsPageViewModel>();
        builder.Services.AddTransient<LanguageBreakdownPageViewModel>();
        builder.Services.AddTransient<SetsPage>();
        builder.Services.AddTransient<SetDetailPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<CollectionPage>();
        builder.Services.AddTransient<CardZoomPage>();
        builder.Services.AddTransient<LanguageBreakdownPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AchievementsPage>();
        builder.Services.AddTransient<TagsPage>();
        builder.Services.AddTransient<StatsPage>();
        builder.Services.AddTransient<CardScanService>();
        builder.Services.AddTransient<ScanTestPageViewModel>();
        builder.Services.AddTransient<ScanTestPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<CatalogPreloader>();
        builder.Services.AddSingleton<DbInitializer>();

        var app = builder.Build();

        // Démarre l'init DB (EnsureCreated + migrations + seed) en tâche de fond dès maintenant —
        // ne pas attendre ici (voir DbInitializer), sous peine d'ANR au démarrage.
        app.Services.GetRequiredService<DbInitializer>();

        // Préchauffe le cache de scoring du Card Scan (voir CardScanService.WarmupAsync) pendant que
        // l'utilisateur navigue dans l'app, pour que le premier scan réel n'en paie pas le coût.
        _ = app.Services.GetRequiredService<CardScanService>().WarmupAsync();

        return app;
    }
}
