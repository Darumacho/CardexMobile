using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Services;
using CardexMobile.Data;
using CommunityToolkit.Maui.Alerts;

namespace CardexMobile.ViewModels;

// Équivalent mobile de l'onglet "Options" de SettingsWindow desktop — scope volontairement réduit
// pour une v1 : pas de légalité de deck (pas de Deck Builder mobile) ni de son d'achievement
// (Achievements mobile pas encore implémenté).
public partial class SettingsPageViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;

    public IReadOnlyList<ColorSwatch> BorderColorSwatches => ColorSwatch.Palette;

    [ObservableProperty] private bool   _showMyCollection;
    [ObservableProperty] private string _collectionBorderColor = "#3a7fc1";
    [ObservableProperty] private string _collectionSyncApiKey = "";
    [ObservableProperty] private bool   _isSyncing;

    public string LastSyncStatusText => _settings.LastCollectionSyncAt is { } at
        ? $"Last synced {at.ToLocalTime():dd/MM/yyyy HH:mm} (from {_settings.LastCollectionSyncOrigin})"
        : "Never synced yet";

    public SettingsPageViewModel(AppSettings settings, MobileAppDbContext db, DbInitializer dbInit)
    {
        _settings = settings;
        _db = db;
        _dbInit = dbInit;
        _showMyCollection = settings.ShowMyCollection;
        _collectionBorderColor = settings.CollectionBorderColor;
        _collectionSyncApiKey = settings.CollectionSyncApiKey ?? "";
    }

    partial void OnShowMyCollectionChanged(bool value)
    {
        _settings.ShowMyCollection = value;
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
            var syncService = new CollectionSyncService(_db, apiKey);
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
}
