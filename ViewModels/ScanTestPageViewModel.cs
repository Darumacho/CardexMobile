using System.Collections.ObjectModel;
using Android.Media;
using CardexMobile.Services;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardexMobile.ViewModels;

// Page "Card Scan" : prend une photo d'une carte (ou scanne en continu via la caméra live), en
// extrait numéro/nom (pipeline complet dans [[CardScanService]]), et propose les correspondances du
// catalogue local avec statut possédé/non et prix. Historique complet des corrections appliquées au
// pipeline dans la mémoire [[project_ocr_scan]].
public partial class ScanTestPageViewModel(CardScanService scanService) : ObservableObject, IDisposable
{
    // Un passage complet (OCR pleine photo, éventuel second passage recadré, scoring sur ~21k
    // cartes) prend facilement 1-2s sur un appareil modeste — un intervalle plus court ferait
    // juste s'empiler les tentatives (déjà gardé par _isLiveScanning) sans gain réel.
    private static readonly TimeSpan LiveScanInterval = TimeSpan.FromSeconds(1.5);

    private IDispatcherTimer? _liveTimer;
    private bool _isLiveScanning;

    // Choisi au démarrage de chaque session live (voir ToggleLiveModeAsync) — "une seule carte"
    // arrête automatiquement après la 1ère confirmation, "plusieurs" continue jusqu'à l'arrêt manuel.
    private bool _stopAfterFirstCard;

    // Assigné par le code-behind une fois le contrôle réalisé (voir ScanTestPage.xaml.cs).
    public CameraView? CameraView { get; set; }

    // Torche manuelle (jamais automatique) — retour utilisateur : le prétraitement
    // luminosité/contraste de CardScanService ne suffit pas en vraie faible luminosité (impossible
    // de récupérer numériquement un détail jamais capturé par le capteur). Pas automatique au
    // démarrage du live scan : la torche aggraverait les reflets holo sur les cartes brillantes,
    // seul l'utilisateur sait si ça aide ou gêne dans sa situation.
    [ObservableProperty] private bool _isTorchOn;

    [RelayCommand]
    private void ToggleTorch()
    {
        if (CameraView is null) return;
        IsTorchOn = !IsTorchOn;
        try
        {
            CameraView.IsTorchOn = IsTorchOn;
        }
        catch
        {
            // Torche non supportée par cette caméra/cet appareil — pas bloquant, on annule juste
            // l'état affiché.
            IsTorchOn = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private string? _photoPath;

    [ObservableProperty] private string _rawText = "";

    // Texte OCR du second passage (recadré/zoomé/contrasté sur la zone du numéro) — uniquement
    // rempli quand le premier passage sur la photo complète n'a pas donné de numéro exploitable.
    // Affiché dans "Show details" à titre de debug, jamais utilisé pour le matching par nom.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumberRegionText))]
    [NotifyPropertyChangedFor(nameof(ShowNumberRegionText))]
    private string? _numberRegionText;

    public bool HasNumberRegionText => NumberRegionText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private string _detectedNumber = "—";

    [ObservableProperty] private string _detectedName = "—";

    // Icône Pokémon à côté du nom détecté (header, pas les lignes de résultat) — résolue en tâche de
    // fond dès que DetectedName change (voir OnDetectedNameChanged ci-dessous).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedNameSprite))]
    private ImageSource? _detectedNameSprite;

    public bool HasDetectedNameSprite => DetectedNameSprite is not null;

    partial void OnDetectedNameChanged(string value) => _ = LoadDetectedNameSpriteAsync(value);

    private async Task LoadDetectedNameSpriteAsync(string name)
    {
        ImageSource? source;
        try
        {
            source = await LocalPokemonSpriteCache.GetSpriteAsync(name);
        }
        catch
        {
            source = null;
        }

        // Si le nom détecté a déjà changé entre-temps (scan live, tick suivant), on ignore ce
        // résultat devenu obsolète plutôt que d'afficher une icône qui ne correspond plus.
        if (DetectedName == name) DetectedNameSprite = source;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNumberRegionText))]
    private bool _isDetailsExpanded;

    [ObservableProperty] private bool _isLiveModeActive;

    public ObservableCollection<CardListItem> Matches { get; } = [];

    // Scan live uniquement : une section par carte distincte confirmée (voir [[project_ocr_scan]]).
    // La photo unique continue d'utiliser juste Matches (pas d'historique pour un scan isolé).
    public ObservableCollection<ScanSection> ScanHistory { get; } = [];

    // Numéro+nom de la dernière carte confirmée (pas forcément la dernière section ouverte si des
    // scans ratés s'intercalent) — sert à ignorer silencieusement les détections identiques
    // consécutives plutôt que de créer une nouvelle section pour la même carte à chaque tick.
    private string? _lastConfirmedKey;

    public bool HasPhoto => !string.IsNullOrEmpty(PhotoPath);
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasNoMatches => DetectedNumber != "—" && DetectedNumber != "Not found" && !IsBusy && Matches.Count == 0;
    public bool ShowNumberRegionText => IsDetailsExpanded && HasNumberRegionText;

    // Détermine l'affichage liste plate (Matches) vs historique de sections (ScanHistory) — basé sur
    // la présence de contenu, pas sur IsLiveModeActive : les sections doivent rester visibles après
    // un arrêt manuel du scan live, jusqu'à la prochaine session live ou une photo unique.
    public bool HasScanHistory => ScanHistory.Count > 0;

    // Une fois numéro ET nom confirmés, la carte est ajoutée à l'historique (scan live) — mais on ne
    // s'arrête plus automatiquement, le scan continue en continu jusqu'à l'arrêt manuel.
    private bool HasConfirmedResult =>
        DetectedNumber is not ("—" or "Not found") && DetectedName is not ("—" or "Not found");

    [RelayCommand]
    private void ToggleDetails() => IsDetailsExpanded = !IsDetailsExpanded;

    // Bouton "Add as Owned" sur une ligne de résultat (photo unique ou section de l'historique live) —
    // même logique de possession que SetDetailPageViewModel, exposée via CardScanService.
    [RelayCommand]
    private async Task IncrementOwnedAsync(CardListItem item)
    {
        item.OwnedQuantity = await scanService.IncrementOwnedAsync(item.CardId, item.SetId);
    }

    // Tap sur une ligne de résultat (liste plate ou dans une section de l'historique live) → set
    // correspondant. Géré ici plutôt que via CollectionView.SelectionChanged côté code-behind car le
    // même template est aussi utilisé dans un BindableLayout (historique live), qui n'a pas de
    // sélection — un seul mécanisme de navigation pour les deux cas.
    [RelayCommand]
    private Task GoToSetAsync(CardListItem item) =>
        Shell.Current.GoToAsync($"{nameof(CardexMobile.Pages.SetDetailPage)}?setId={item.SetId}&setName={Uri.EscapeDataString(item.SetName)}");

    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        if (IsBusy) return;
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ErrorMessage = "Camera capture not supported on this device/emulator.";
            return;
        }

        var photo = await MediaPicker.Default.CapturePhotoAsync();
        if (photo is null) return;
        await ProcessPhotoAsync(photo);
    }

    // Contourne la caméra (webcam d'émulateur de mauvaise qualité, p. ex.) : permet de pousser une
    // photo prise avec un vrai téléphone (via adb push / glisser-déposer dans la fenêtre d'émulateur)
    // et de la choisir depuis la galerie pour tester le même pipeline OCR sur une image nette.
    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        if (IsBusy) return;
        var photo = await MediaPicker.Default.PickPhotoAsync();
        if (photo is null) return;
        await ProcessPhotoAsync(photo);
    }

    private async Task ProcessPhotoAsync(FileResult photo)
    {
        ErrorMessage = null;
        try
        {
            IsBusy = true;
            RawText = "";
            NumberRegionText = null;
            DetectedNumber = "—";
            DetectedName = "—";
            Matches.Clear();

            // Une photo unique ne fait pas partie de l'historique de scan live — repart sur
            // l'affichage plat classique même si une session live précédente a laissé des sections.
            ScanHistory.Clear();
            OnPropertyChanged(nameof(HasScanHistory));

            var localPath = System.IO.Path.Combine(FileSystem.CacheDirectory, $"scan_{DateTime.UtcNow.Ticks}.jpg");
            using (var sourceStream = await photo.OpenReadAsync())
            using (var localStream = File.Create(localPath))
                await sourceStream.CopyToAsync(localStream);
            PhotoPath = localPath;

            var bytes = await File.ReadAllBytesAsync(localPath);
            await ApplyOutcomeAsync(bytes);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoMatches));
        }
    }

    [RelayCommand]
    private async Task ToggleLiveModeAsync()
    {
        if (IsLiveModeActive)
        {
            StopLiveMode();
            return;
        }

        if (CameraView is null) return;

        var choice = await Shell.Current.DisplayActionSheet(
            "Scan a single card or multiple cards?", "Cancel", null, "Single card", "Multiple cards");
        if (choice is null or "Cancel") return;
        _stopAfterFirstCard = choice == "Single card";

        ErrorMessage = null;
        try
        {
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                ErrorMessage = "Camera permission denied.";
                return;
            }

            // Sans sélection explicite, l'émulateur choisit parfois la caméra avant (décor
            // "VirtualScene" par défaut) au lieu de l'arrière (webcam branchée) — observé en test.
            var cameras = await CameraView.GetAvailableCameras(CancellationToken.None);
            var rearCamera = cameras.FirstOrDefault(c => c.Position == CameraPosition.Rear);
            if (rearCamera is not null)
            {
                CameraView.SelectedCamera = rearCamera;

                // Par défaut (Size.Zero) la caméra garde le format déjà en place, pas forcément le
                // plus net disponible — viser la résolution de capture la plus haute supportée pour
                // donner le plus de détail possible au numéro/nom avant l'OCR.
                var maxResolution = rearCamera.SupportedResolutions
                    .OrderByDescending(r => r.Width * r.Height)
                    .FirstOrDefault();
                if (maxResolution != default)
                    CameraView.ImageCaptureResolution = maxResolution;
            }

            await CameraView.StartCameraPreview(CancellationToken.None);
            IsLiveModeActive = true;

            // Sinon l'aperçu statique d'une photo prise/choisie juste avant reste affiché (lié à
            // HasPhoto), par-dessus/à côté du flux caméra live — bug observé en test.
            PhotoPath = null;

            // Nouvelle session de scan en continu : repartir d'un historique vierge.
            ScanHistory.Clear();
            OnPropertyChanged(nameof(HasScanHistory));
            _lastConfirmedKey = null;

            _liveTimer = Application.Current!.Dispatcher.CreateTimer();
            _liveTimer.Interval = LiveScanInterval;
            _liveTimer.Tick += async (_, _) => await LiveScanOnceAsync();
            _liveTimer.Start();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void StopLiveMode()
    {
        _liveTimer?.Stop();
        _liveTimer = null;
        CameraView?.StopCameraPreview();
        IsLiveModeActive = false;

        // Ne jamais laisser la torche allumée après la fermeture du scan live.
        if (IsTorchOn)
        {
            IsTorchOn = false;
            try { if (CameraView is not null) CameraView.IsTorchOn = false; } catch { }
        }

        // Une fois le scan arrêté, on veut pouvoir tout relire sans rouvrir section par section —
        // le repli automatique n'a d'intérêt que pendant le scan actif.
        foreach (var section in ScanHistory)
            section.IsExpanded = true;
    }

    private async Task LiveScanOnceAsync()
    {
        if (_isLiveScanning || CameraView is null) return;
        _isLiveScanning = true;
        try
        {
            // Filet de sécurité : si CaptureImage() ne se termine jamais (ni succès ni exception —
            // observé en test, une seule capture déclenchée puis plus aucune ensuite), _isLiveScanning
            // resterait bloqué à true pour toujours et bloquerait silencieusement tous les scans
            // suivants. Un timeout garantit qu'on relâche le verrou même dans ce cas.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var stream = await CameraView.CaptureImage(cts.Token);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            await ApplyOutcomeAsync(memoryStream.ToArray());

            // Numéro ET nom confirmés : ajoute une section à l'historique si c'est une carte
            // distincte de la précédente confirmation (ignore silencieusement les répétitions
            // consécutives de la même carte pendant qu'elle reste dans le cadre).
            if (HasConfirmedResult)
            {
                var key = $"{DetectedNumber}|{DetectedName}";
                if (key != _lastConfirmedKey)
                {
                    _lastConfirmedKey = key;
                    foreach (var section in ScanHistory)
                        section.IsExpanded = false;
                    ScanHistory.Add(new ScanSection($"{DetectedName} · #{DetectedNumber}", Matches) { IsExpanded = true });
                    OnPropertyChanged(nameof(HasScanHistory));

                    _ = Toast.Make($"{DetectedName} {DetectedNumber} scanned!").Show();
                    PlayScanSound();

                    // Mode "une seule carte" choisi au démarrage : on s'arrête dès la 1ère
                    // confirmation plutôt que de continuer en scan en série.
                    if (_stopAfterFirstCard) StopLiveMode();
                }
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Camera capture timed out.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _isLiveScanning = false;
        }
    }

    // Petit retour sonore à la confirmation d'une carte — joue le son de notification système par
    // défaut, sans asset ni dépendance supplémentaire (RingtoneManager est déjà disponible, l'app
    // étant Android-only). Best-effort : un appareil sans son de notification configuré ne doit
    // jamais faire planter le scan.
    private static void PlayScanSound()
    {
        try
        {
            var uri = RingtoneManager.GetDefaultUri(RingtoneType.Notification);
            RingtoneManager.GetRingtone(Android.App.Application.Context, uri)?.Play();
        }
        catch
        {
            // Pas de son disponible/jouable sur cet appareil — tant pis, pas bloquant.
        }
    }

    private async Task ApplyOutcomeAsync(byte[] photoBytes)
    {
        var outcome = await scanService.ScanAsync(photoBytes);

        RawText = outcome.RawText;
        NumberRegionText = outcome.NumberRegionText;
        DetectedNumber = outcome.DetectedNumber;
        DetectedName = outcome.DetectedName;
        Matches.Clear();
        foreach (var item in outcome.Matches)
        {
            Matches.Add(item);

            // Best-effort, en tâche de fond : l'icône se remplit dès que prête sans retarder
            // l'affichage des résultats (lecture locale, mais pas la peine d'attendre pour autant).
            _ = LoadItemSpriteAsync(item);
        }
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private static async Task LoadItemSpriteAsync(CardListItem item)
    {
        try
        {
            item.SpriteSource = await LocalPokemonSpriteCache.GetSpriteAsync(item.Name);
        }
        catch
        {
            // Purement cosmétique — ne doit jamais faire échouer l'affichage des résultats.
        }
    }

    public void Dispose() => StopLiveMode();
}
