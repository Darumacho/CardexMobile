using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Services;
using CardexMobile.Data;
using CardexMobile.Services;
using CommunityToolkit.Maui.Alerts;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

// Équivalent mobile de l'onglet "Options" de SettingsWindow desktop — scope volontairement réduit
// pour une v1 : pas de légalité de deck (pas de Deck Builder mobile) ni de son d'achievement
// (Achievements mobile pas encore implémenté).
public partial class SettingsPageViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;
    private readonly MobileUpdateService _updateService;

    public IReadOnlyList<ColorSwatch> BorderColorSwatches => ColorSwatch.Palette;
    public ObservableCollection<LanguageOptionViewModel> LanguageOptions { get; } = [];

    [ObservableProperty] private bool   _showMyCollection;
    [ObservableProperty] private bool   _showStats;
    [ObservableProperty] private bool   _statsTeamMode;
    [ObservableProperty] private string _collectionBorderColor = "#3a7fc1";
    [ObservableProperty] private string _collectionSyncApiKey = "";
    [ObservableProperty] private bool   _isSyncing;
    [ObservableProperty] private MobileUpdateInfo? _pendingUpdate;
    [ObservableProperty] private bool   _isUpdating;
    [ObservableProperty] private int    _updateProgress;

    public bool HasUpdate => PendingUpdate is not null;
    public double UpdateProgressFraction => UpdateProgress / 100.0;
    partial void OnPendingUpdateChanged(MobileUpdateInfo? value) => OnPropertyChanged(nameof(HasUpdate));
    partial void OnUpdateProgressChanged(int value) => OnPropertyChanged(nameof(UpdateProgressFraction));

    public string LastSyncStatusText => _settings.LastCollectionSyncAt is { } at
        ? $"Last synced {at.ToLocalTime():dd/MM/yyyy HH:mm} (from {_settings.LastCollectionSyncOrigin})"
        : "Never synced yet";

    public SettingsPageViewModel(AppSettings settings, MobileAppDbContext db, DbInitializer dbInit, MobileUpdateService updateService)
    {
        _settings = settings;
        _db = db;
        _dbInit = dbInit;
        _updateService = updateService;
        _showMyCollection = settings.ShowMyCollection;
        _showStats = settings.ShowStats;
        _statsTeamMode = settings.StatsTeamMode;
        _collectionBorderColor = settings.CollectionBorderColor;
        _collectionSyncApiKey = settings.CollectionSyncApiKey ?? "";

        foreach (var (code, label) in LanguageCatalog.All)
        {
            var opt = new LanguageOptionViewModel(code, label, settings.TrackedLanguages.Contains(code), isLocked: code == "EN");
            opt.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LanguageOptionViewModel.IsTracked))
                    OnLanguageOptionToggled();
            };
            LanguageOptions.Add(opt);
        }
    }

    private void OnLanguageOptionToggled()
    {
        // L'anglais reste toujours suivi (données existantes implicitement en anglais) —
        // défensif, la case est aussi désactivée côté UI.
        var en = LanguageOptions.First(o => o.Code == "EN");
        if (!en.IsTracked) en.IsTracked = true;

        _settings.TrackedLanguages = LanguageOptions.Where(o => o.IsTracked).Select(o => o.Code).ToList();
        _settings.Save();
        // Contrairement au desktop (sets déjà ouverts gardés en mémoire), SetDetailPageViewModel
        // est recréé à chaque navigation (AddTransient) et lit IsMultiLanguageMode dynamiquement
        // depuis _settings — pas besoin de forcer un rechargement ici.
    }

    public async Task CheckForUpdateAsync()
    {
        PendingUpdate = await _updateService.CheckAsync();
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (PendingUpdate is null) return;
        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            var progress = new Progress<int>(p => UpdateProgress = p);
            await _updateService.DownloadAndInstallAsync(PendingUpdate, progress);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Update failed", ex.Message, "OK");
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private bool CanInstallUpdate() => !IsUpdating;

    partial void OnIsUpdatingChanged(bool value) => InstallUpdateCommand.NotifyCanExecuteChanged();

    partial void OnShowMyCollectionChanged(bool value)
    {
        _settings.ShowMyCollection = value;
        _settings.Save();
    }

    partial void OnShowStatsChanged(bool value)
    {
        _settings.ShowStats = value;
        _settings.Save();
    }

    partial void OnStatsTeamModeChanged(bool value)
    {
        _settings.StatsTeamMode = value;
        _settings.Save();
    }

    partial void OnCollectionSyncApiKeyChanged(string value)
    {
        // Trim défensif : une clé copiée-collée depuis l'email de vérification embarque facilement
        // un espace ou un retour à la ligne, ce qui casse la comparaison exacte côté serveur et se
        // traduit par un 401 trompeur.
        _settings.CollectionSyncApiKey = value.Trim();
        _settings.Save();
    }

    [RelayCommand]
    private void SetBorderColor(string hex)
    {
        CollectionBorderColor = hex;
        _settings.CollectionBorderColor = hex;
        _settings.Save();
    }

    // Synchronisation manuelle : pousse l'état local vers l'API Cardex, taggé "mobile". Si le
    // serveur montre que la dernière synchro connue vient du desktop et est plus récente que notre
    // dernier sync connu, on prévient avant d'écraser (décision utilisateur : avertir, ne pas
    // bloquer) — contrairement au rapatriement automatique au démarrage (CollectionSyncStartup)
    // qui applique directement sans demander.
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        var apiKey = _settings.CollectionSyncApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await Shell.Current.DisplayAlert("Sync", "Enter your Cardex API key first (get one at cardex-api.dev).", "OK");
            return;
        }

        IsSyncing = true;
        try
        {
            await _dbInit.ReadyTask;
            var syncService = new CollectionSyncService(_db, _settings, apiKey);
            var remote = await syncService.FetchRemoteStateAsync();

            if (remote is not null && remote.Origin != "mobile")
            {
                var remoteUpdatedAt = DateTime.Parse(remote.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
                if (_settings.LastCollectionSyncAt is null || remoteUpdatedAt > _settings.LastCollectionSyncAt)
                {
                    var confirmed = await Shell.Current.DisplayAlert(
                        "Sync — overwrite warning",
                        $"The last sync came from {remote.Origin} on {remoteUpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}.\n" +
                        "Syncing now will overwrite those changes with your local collection.\n\nContinue?",
                        "Continue", "Cancel");
                    if (!confirmed) return;
                }
            }

            var updatedAt = await syncService.PushAsync("mobile");
            _settings.LastCollectionSyncAt = updatedAt;
            _settings.LastCollectionSyncOrigin = "mobile";
            _settings.Save();
            OnPropertyChanged(nameof(LastSyncStatusText));

            // Toast plutôt que juste le label de la page Settings : l'action est aussi
            // déclenchable depuis le burger menu, sans être sur cette page.
            _ = Toast.Make("🔄 Collection synced.").Show();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Sync failed", ex.Message, "OK");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private static string SyncBackupsFolder => Path.Combine(FileSystem.AppDataDirectory, "SyncBackups");

    // Filet de sécurité demandé après l'incident du 2026-10-15 : permet d'annuler un import de
    // sync malvenu en restaurant le backup local pris juste avant (voir
    // BackupLocalCollectionBeforeSyncAsync dans SetsPage.xaml.cs, qui écrit dans ce même dossier).
    // N'a pas besoin de clé API ni de réseau — lit uniquement un fichier local déjà présent.
    [RelayCommand]
    private async Task RestoreLastSyncBackupAsync()
    {
        var latest = Directory.Exists(SyncBackupsFolder)
            ? new DirectoryInfo(SyncBackupsFolder).GetFiles("pre_sync_*.json").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()
            : null;
        if (latest is null)
        {
            await Shell.Current.DisplayAlert("Restore backup", "No sync backup found.", "OK");
            return;
        }

        var json = await File.ReadAllTextAsync(latest.FullName);
        var ownedCount = CollectionSyncService.CountOwnedCards(json);

        var confirmed = await Shell.Current.DisplayAlert(
            "Restore backup",
            $"Restore the backup from {latest.LastWriteTime:dd/MM/yyyy HH:mm} ({ownedCount} owned card(s))?\n\n" +
            "This will replace your current collection, wants, tags and achievements.",
            "Restore", "Cancel");
        if (!confirmed) return;

        await _dbInit.ReadyTask;
        var syncService = new CollectionSyncService(_db, _settings, string.Empty);
        await syncService.ApplyCollectionJsonAsync(json);

        _ = Toast.Make($"Collection restored from backup ({latest.LastWriteTime:dd/MM/yyyy HH:mm}).").Show();
    }
}
