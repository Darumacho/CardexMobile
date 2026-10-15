using Cardex.Services;
using CardexMobile.Data;
using CardexMobile.ViewModels;
using CommunityToolkit.Maui.Alerts;

namespace CardexMobile.Pages;

public partial class SetsPage : ContentPage
{
    private readonly SetsPageViewModel _vm;
    private readonly AppSettings _settings;
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;
    private bool _syncPromptChecked;

    public SetsPage(SetsPageViewModel vm, AppSettings settings, MobileAppDbContext db, DbInitializer dbInit)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        _db = db;
        _dbInit = dbInit;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.Groups.Count == 0)
            await _vm.LoadAsync();
        else
            await _vm.RefreshCountsAsync();

        // Une seule fois par session, sur le premier écran affiché — Shell.Current n'est pas
        // garanti disponible plus tôt (démarrage de l'app, voir DbInitializer). Voir incident
        // 2026-10-15 : plus jamais appliqué sans confirmation explicite.
        if (!_syncPromptChecked)
        {
            _syncPromptChecked = true;
            _ = CheckPendingSyncAsync();
        }
    }

    private async Task CheckPendingSyncAsync()
    {
        var apiKey = _settings.CollectionSyncApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        try
        {
            await _dbInit.ReadyTask;
            var syncService = new CollectionSyncService(_db, apiKey);
            var remote = await syncService.FetchRemoteStateAsync();
            if (remote is null || remote.Origin == "mobile") return;

            var remoteUpdatedAt = DateTime.Parse(remote.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (_settings.LastCollectionSyncAt is { } lastKnown && remoteUpdatedAt <= lastKnown) return;

            var ownedCount = CollectionSyncService.CountOwnedCards(remote.CollectionJson);

            var confirmed = await Shell.Current.DisplayAlert(
                "Sync available",
                $"A sync from {remote.Origin} is available, from {remoteUpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm} " +
                $"({ownedCount} owned card(s)).\n\n" +
                "Import it now? Your current local collection will be backed up first, just in case.",
                "Import", "Not now");
            if (!confirmed) return;

            var backupPath = await BackupLocalCollectionBeforeSyncAsync(syncService);

            await syncService.ApplyCollectionJsonAsync(remote.CollectionJson);
            _settings.LastCollectionSyncAt = remoteUpdatedAt;
            _settings.LastCollectionSyncOrigin = remote.Origin;
            _settings.Save();

            await _vm.LoadAsync();
            _ = Toast.Make($"Collection synced from {remote.Origin} (backup saved).").Show();
            System.Diagnostics.Debug.WriteLine($"Pre-sync backup saved to {backupPath}");
        }
        catch { /* best-effort, ne doit jamais bloquer l'app */ }
    }

    private static async Task<string> BackupLocalCollectionBeforeSyncAsync(CollectionSyncService syncService)
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "SyncBackups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"pre_sync_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var json = await syncService.BuildCollectionJsonAsync();
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private async void OnSetItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: SetItem set })
            await Shell.Current.GoToAsync($"{nameof(SetDetailPage)}?setId={set.SetId}&setName={Uri.EscapeDataString(set.Name)}");
    }
}
