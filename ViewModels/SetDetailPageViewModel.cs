using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Models;
using Cardex.Services;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

public partial class CardItem : ObservableObject
{
    public string  CardId        { get; init; } = "";
    public string  Name          { get; init; } = "";
    public string  Number        { get; init; } = "";
    public string  SetId         { get; init; } = "";
    public string  ImageUrl      { get; init; } = "";
    public string? ImageLargeUrl { get; init; }
    public string? Rarity        { get; init; }
    public decimal? CmLow  { get; init; }
    public decimal? TcgLow { get; init; }
    public string? CmUrl  { get; init; }
    public string? TcgUrl { get; init; }

    public string CmText  => CmLow.HasValue  ? $"€{CmLow.Value:F2}"  : "—";
    public string TcgText => TcgLow.HasValue ? $"${TcgLow.Value:F2}" : "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwned))]
    private int _quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isWanted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludedOpacity))]
    private bool _isExcluded;

    public bool   IsOwned         => Quantity > 0;
    public bool   HasRarity       => Rarity is not null;
    public string FavoriteIcon    => IsWanted ? "★" : "☆";
    public double ExcludedOpacity => IsExcluded ? 0.45 : 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagLabel))]
    [NotifyPropertyChangedFor(nameof(TagDotColor))]
    private TagItem? _selectedTag;

    public string TagLabel    => SelectedTag?.Name ?? "+ Tag";
    public Microsoft.Maui.Graphics.Color TagDotColor => SelectedTag?.SwatchColor ?? Microsoft.Maui.Graphics.Colors.Transparent;

    // Callbacks set by the ViewModel — keeps DB logic out of the model
    public Func<CardItem, Task>? OnToggleFavorite  { get; set; }
    public Func<CardItem, Task>? OnToggleExcluded  { get; set; }
    public Func<CardItem, Task>? OnIncrement       { get; set; }
    public Func<CardItem, Task>? OnDecrement       { get; set; }
    public Func<CardItem, Task>? OnToggleOwned     { get; set; }
    public Func<CardItem, Task>? OnOpenHd          { get; set; }
    public Func<CardItem, TagItem?, Task>? OnTagChanged { get; set; }

    partial void OnSelectedTagChanged(TagItem? value)
    {
        if (OnTagChanged is not null) _ = OnTagChanged(this, value);
    }

    [RelayCommand] private Task ToggleFavorite() => OnToggleFavorite?.Invoke(this) ?? Task.CompletedTask;
    [RelayCommand] private Task ToggleExcluded() => OnToggleExcluded?.Invoke(this) ?? Task.CompletedTask;
    [RelayCommand] private Task Increment()      => OnIncrement?.Invoke(this)      ?? Task.CompletedTask;
    [RelayCommand] private Task Decrement()      => OnDecrement?.Invoke(this)      ?? Task.CompletedTask;
    [RelayCommand] private Task ToggleOwned()    => OnToggleOwned?.Invoke(this)    ?? Task.CompletedTask;
    [RelayCommand] private Task OpenHd()         => OnOpenHd?.Invoke(this)         ?? Task.CompletedTask;

    // Pas d'URL directe pour certains sets de secours (TCGdex) : on retombe sur une recherche par
    // nom + set + numéro, même format que CardViewModel.OpenCmLink/OpenTcgLink côté desktop.
    private string SearchQuery => Uri.EscapeDataString($"{Name} {SetId.ToUpperInvariant()} {Number}");

    [RelayCommand]
    private Task OpenCmLink()
    {
        var url = !string.IsNullOrEmpty(CmUrl) ? CmUrl
            : $"https://www.cardmarket.com/en/Pokemon/Products/Search?searchString={SearchQuery}";
        return Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(url);
    }

    [RelayCommand]
    private Task OpenTcgLink()
    {
        var url = !string.IsNullOrEmpty(TcgUrl) ? TcgUrl
            : $"https://www.tcgplayer.com/search/pokemon/product?productLineName=pokemon&view=grid&q={SearchQuery}";
        return Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(url);
    }
}

public partial class SetDetailPageViewModel : ObservableObject, IQueryAttributable
{
    private readonly MobileAppDbContext _db;
    private readonly PokemonTcgService  _tcgService;
    private readonly DbInitializer      _dbInit;
    private string _setSeries = "";

    public ObservableCollection<CardItem> Cards { get; } = [];
    public ObservableCollection<TagItem> Tags { get; } = [];
    public bool HasTags => Tags.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCards))]
    [NotifyPropertyChangedFor(nameof(HasNoCards))]
    private string _searchText = "";

    // Masque les cartes possédées de la liste — demandé pour les gros sets où faire défiler
    // jusqu'aux cartes manquantes devient pénible. Réinitialisé à chaque visite du set, comme
    // IsBinderView, pas persisté.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCards))]
    [NotifyPropertyChangedFor(nameof(HasNoCards))]
    [NotifyPropertyChangedFor(nameof(HideOwnedLabel))]
    private bool _hideOwned;

    public string HideOwnedLabel => HideOwned ? "Unhide owned" : "Hide owned";

    [RelayCommand]
    private void ToggleHideOwned() => HideOwned = !HideOwned;

    [ObservableProperty] private string _setName    = "";
    [ObservableProperty] private string _setId      = "";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _loadingText = "Loading…";
    [ObservableProperty] private int    _ownedCount;
    [ObservableProperty] private int    _total;
    [ObservableProperty] private bool   _isSetFavorite;
    [ObservableProperty] private bool   _isBinderView;
    [ObservableProperty] private string _logoUrl = "";

    public string CompletionText   => $"{OwnedCount} / {Total}";
    public double CompletionRatio  => Total > 0 ? (double)OwnedCount / Total : 0;
    public string SetFavoriteIcon  => IsSetFavorite ? "★" : "☆";

    // Même sémantique que SetViewModel.AllOwned côté desktop.
    public bool AllOwned => Cards.Count > 0 && Cards.All(c => c.IsOwned);
    public string ToggleAllLabel => AllOwned ? "Uncheck all" : "Check all";

    // N'affiche "No cards found" qu'une fois le chargement terminé — CollectionView.EmptyView
    // s'affiche dès que FilteredCards est vide, y compris pendant le chargement initial sinon.
    public bool HasNoCards => !IsBusy && FilteredCards.Count == 0;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(HasNoCards));

    // Message affiché à la place de "No cards found" si le premier chargement (fetch live
    // pokemontcg.io, pour un set jamais mis en cache) a échoué — voir le catch dans LoadAsync.
    [ObservableProperty] private string? _errorMessage;
    public string EmptyStateText => ErrorMessage ?? "No cards found";
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(EmptyStateText));

    // Logo du set affiché dans la barre de titre à la place du nom quand disponible.
    public bool HasLogo => !string.IsNullOrEmpty(LogoUrl);
    public bool NoLogo  => !HasLogo;

    partial void OnLogoUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasLogo));
        OnPropertyChanged(nameof(NoLogo));
    }

    public bool IsListView => !IsBinderView;

    partial void OnIsBinderViewChanged(bool value) => OnPropertyChanged(nameof(IsListView));

    [RelayCommand]
    private void ToggleViewMode() => IsBinderView = !IsBinderView;

    partial void OnIsSetFavoriteChanged(bool value) => OnPropertyChanged(nameof(SetFavoriteIcon));

    public IReadOnlyList<CardItem> FilteredCards
    {
        get
        {
            IEnumerable<CardItem> cards = Cards;
            if (HideOwned) cards = cards.Where(c => !c.IsOwned);

            if (string.IsNullOrWhiteSpace(SearchText)) return cards as IReadOnlyList<CardItem> ?? cards.ToList();
            var q = SearchText.Trim();
            return cards.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Number.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public SetDetailPageViewModel(MobileAppDbContext db, PokemonTcgService tcgService, DbInitializer dbInit)
    {
        _db = db;
        _tcgService = tcgService;
        _dbInit = dbInit;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("setId",   out var sid))   SetId   = sid?.ToString() ?? "";
        if (query.TryGetValue("setName", out var sname)) SetName = Uri.UnescapeDataString(sname?.ToString() ?? "");
        Cards.Clear();
        SearchText = "";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(SetId) || IsBusy) return;
        IsBusy = true;
        LoadingText = "Loading…";
        ErrorMessage = null;
        try
        {
            await _dbInit.ReadyTask;
            IsSetFavorite = await _db.FavoriteSets.AnyAsync(f => f.SetId == SetId);
            var setMeta = await _db.CachedSets.Where(s => s.SetId == SetId).Select(s => new { s.LogoUrl, s.Series }).FirstOrDefaultAsync();
            LogoUrl = setMeta?.LogoUrl ?? "";
            _setSeries = setMeta?.Series ?? "";

            Tags.Clear();
            foreach (var t in await _db.Tags.OrderBy(t => t.Id).ToListAsync())
                Tags.Add(new TagItem { Id = t.Id, Name = t.Name, Color = t.Color });
            OnPropertyChanged(nameof(HasTags));

            var cached = await _db.CachedCards
                .Where(c => c.SetId == SetId)
                .OrderBy(c => c.SortOrder)
                .ToListAsync();

            // Sets connus pour une pagination cassée côté pokemontcg.io (ex. Ascended Heroes) —
            // reconcilié avec le seed de secours avant tout fetch live. Retourne null si ce set
            // n'est pas concerné (immense majorité des cas), auquel cas rien ne change ici.
            var reconciled = await CompletionSeeder.ReconcileAsync(_db, SetId, cached);
            if (reconciled is not null) cached = reconciled;

            if (cached.Count == 0)
            {
                var progress = new Progress<int>(n => LoadingText = $"Loading… {n} cards");
                var apiCards = await _tcgService.GetCardsAsync(SetId, progress);
                var order = 0;
                cached = apiCards.Select(c => new CachedCard
                {
                    CardId = c.Id, SetId = SetId, Name = c.Name, Number = c.Number,
                    ImageSmall = c.Images.Small, ImageLarge = c.Images.Large,
                    Rarity = c.Rarity, SortOrder = order++,
                    CmLow  = c.Cardmarket?.Prices?.LowPrice,
                    TcgLow = c.Tcgplayer?.Prices?.Values.FirstOrDefault()?.Low,
                    CmUrl  = c.Cardmarket?.Url,
                    TcgUrl = c.Tcgplayer?.Url
                }).ToList();
                _db.CachedCards.AddRange(cached);
                await _db.SaveChangesAsync();
            }

            var cardIds = cached.Select(c => c.CardId).ToList();

            var owned    = await _db.OwnedCards   .Where(o => cardIds.Contains(o.CardId)).ToDictionaryAsync(o => o.CardId, o => o.Quantity);
            var wanted   = (await _db.WantedCards  .Where(w => cardIds.Contains(w.CardId)).Select(w => w.CardId).ToListAsync()).ToHashSet();
            var excluded = (await _db.ExcludedCards.Where(e => cardIds.Contains(e.CardId)).Select(e => e.CardId).ToListAsync()).ToHashSet();
            var cardTags = await _db.CardTags.Where(ct => cardIds.Contains(ct.CardId)).ToDictionaryAsync(ct => ct.CardId, ct => ct.TagId);

            Cards.Clear();
            foreach (var c in cached)
            {
                var item = new CardItem
                {
                    CardId = c.CardId, Name = c.Name, Number = c.Number,
                    SetId = c.SetId, ImageUrl = c.ImageSmall, ImageLargeUrl = c.ImageLarge ?? c.ImageSmall,
                    Rarity = c.Rarity, CmLow = c.CmLow, TcgLow = c.TcgLow, CmUrl = c.CmUrl, TcgUrl = c.TcgUrl,
                    Quantity   = owned.GetValueOrDefault(c.CardId),
                    IsWanted   = wanted.Contains(c.CardId),
                    IsExcluded = excluded.Contains(c.CardId),
                    // Tag initial posé avant de câbler OnTagChanged ci-dessous, pour ne pas
                    // déclencher d'écriture DB pendant la construction (même piège que desktop).
                    SelectedTag = cardTags.TryGetValue(c.CardId, out var tagId)
                        ? Tags.FirstOrDefault(t => t.Id == tagId)
                        : null
                };
                item.OnToggleFavorite = ToggleFavoriteAsync;
                item.OnToggleExcluded = ToggleExcludedAsync;
                item.OnIncrement      = IncrementAsync;
                item.OnDecrement      = DecrementAsync;
                item.OnToggleOwned    = ToggleOwnedAsync;
                item.OnOpenHd         = NavigateToZoomAsync;
                item.OnTagChanged     = OnCardTagChangedAsync;
                Cards.Add(item);
            }

            RefreshCompletion();
            OnPropertyChanged(nameof(FilteredCards));
            OnPropertyChanged(nameof(HasNoCards));
        }
        catch (Exception ex)
        {
            // Sans ce catch, une erreur réseau/API lors du tout premier chargement d'un set (fetch
            // live pokemontcg.io, pas encore en cache) remontait telle quelle jusqu'à OnAppearing
            // (async void) et plantait toute l'app — desktop catch déjà ce genre d'erreur à tous
            // ses points d'appel équivalents (MainViewModel), ce point-ci ne le faisait pas.
            System.Diagnostics.Debug.WriteLine($"SetDetailPageViewModel.LoadAsync failed: {ex}");
            ErrorMessage = "Couldn't load this set. Check your connection and try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleSetFavoriteAsync()
    {
        IsSetFavorite = !IsSetFavorite;
        var entry = await _db.FavoriteSets.FindAsync(SetId);
        if (IsSetFavorite) { if (entry is null) _db.FavoriteSets.Add(new FavoriteSet { SetId = SetId }); }
        else if (entry is not null) _db.FavoriteSets.Remove(entry);
        await _db.SaveChangesAsync();
    }

    private async Task ToggleFavoriteAsync(CardItem card)
    {
        card.IsWanted = !card.IsWanted;
        var entry = await _db.WantedCards.FindAsync(card.CardId);
        if (card.IsWanted) { if (entry is null) _db.WantedCards.Add(new WantedCard { CardId = card.CardId, SetId = card.SetId }); }
        else if (entry is not null) _db.WantedCards.Remove(entry);
        await _db.SaveChangesAsync();
        if (card.IsWanted) await AchievementChecks.CheckWantedAsync(_db);
    }

    private async Task ToggleExcludedAsync(CardItem card)
    {
        card.IsExcluded = !card.IsExcluded;
        var entry = await _db.ExcludedCards.FindAsync(card.CardId);
        if (card.IsExcluded) { if (entry is null) _db.ExcludedCards.Add(new ExcludedCard { CardId = card.CardId, SetId = card.SetId }); }
        else if (entry is not null) _db.ExcludedCards.Remove(entry);
        await _db.SaveChangesAsync();
        RefreshCompletion();
        if (card.IsExcluded) await AchievementChecks.CheckExcludedAsync(_db);
    }

    private async Task IncrementAsync(CardItem card)
    {
        card.Quantity++;
        await SaveOwnedAsync(card);
        RefreshCompletion();
    }

    private async Task DecrementAsync(CardItem card)
    {
        if (card.Quantity <= 0) return;
        card.Quantity--;
        await SaveOwnedAsync(card);
        RefreshCompletion();
    }

    // Même sémantique que CardViewModel.IsOwned côté desktop : bascule plutôt qu'incrémente, pour
    // le tap en vue binder.
    private async Task ToggleOwnedAsync(CardItem card)
    {
        card.Quantity = card.Quantity > 0 ? 0 : 1;
        await SaveOwnedAsync(card);
        RefreshCompletion();
    }

    private async Task SaveOwnedAsync(CardItem card)
    {
        var entry = await _db.OwnedCards.FindAsync(card.CardId);
        if (card.Quantity > 0)
        {
            if (entry is null) _db.OwnedCards.Add(new OwnedCard { CardId = card.CardId, SetId = card.SetId, Quantity = card.Quantity });
            else entry.Quantity = card.Quantity;
        }
        else if (entry is not null) _db.OwnedCards.Remove(entry);
        await _db.SaveChangesAsync();
        await AchievementChecks.CheckOwnershipAsync(_db, card.Quantity);
    }

    private async Task OnCardTagChangedAsync(CardItem card, TagItem? tag)
    {
        var entry = await _db.CardTags.FindAsync(card.CardId);
        if (tag is not null)
        {
            if (entry is null) _db.CardTags.Add(new CardTag { CardId = card.CardId, TagId = tag.Id });
            else entry.TagId = tag.Id;
        }
        else if (entry is not null)
        {
            _db.CardTags.Remove(entry);
        }
        await _db.SaveChangesAsync();
    }

    private static Task NavigateToZoomAsync(CardItem card)
    {
        var imageUrl = card.ImageLargeUrl ?? card.ImageUrl;
        return Shell.Current.GoToAsync($"{nameof(CardexMobile.Pages.CardZoomPage)}" +
            $"?imageUrl={Uri.EscapeDataString(imageUrl)}" +
            $"&name={Uri.EscapeDataString(card.Name)}" +
            $"&number={Uri.EscapeDataString(card.Number)}" +
            $"&rarity={Uri.EscapeDataString(card.Rarity ?? "")}");
    }

    private void RefreshCompletion()
    {
        var nonExcluded = Cards.Where(c => !c.IsExcluded).ToList();
        Total      = nonExcluded.Count;
        OwnedCount = nonExcluded.Count(c => c.IsOwned);
        OnPropertyChanged(nameof(CompletionText));
        OnPropertyChanged(nameof(CompletionRatio));
        OnPropertyChanged(nameof(AllOwned));
        OnPropertyChanged(nameof(ToggleAllLabel));
        // Si "Hide owned" est actif, une carte qui vient d'être (dés)possédée doit
        // apparaître/disparaître de la liste, pas juste mettre à jour son état visuel.
        OnPropertyChanged(nameof(FilteredCards));
        OnPropertyChanged(nameof(HasNoCards));

        if (Total > 0 && OwnedCount == Total)
            _ = AchievementChecks.CheckSetCompletionAsync(_db, SetId, SetName, _setSeries, Total);
    }

    // Même logique que MainViewModel.ToggleAllOwnedAsync côté desktop : bascule toutes les cartes
    // du set (sauf celles exclues) d'un coup, une seule écriture DB.
    [RelayCommand]
    private async Task ToggleAllOwnedAsync()
    {
        if (Cards.Count == 0 || IsBusy) return;

        var targets = Cards.Where(c => !c.IsExcluded).ToList();
        var targetState = !AllOwned;

        if (targetState)
        {
            var existingIds = (await _db.OwnedCards
                .Where(o => o.SetId == SetId)
                .Select(o => o.CardId)
                .ToListAsync()).ToHashSet();

            foreach (var card in targets)
            {
                var wasOwned = card.Quantity > 0;
                if (!wasOwned) card.Quantity = 1;
                if (!existingIds.Contains(card.CardId))
                    _db.OwnedCards.Add(new OwnedCard { CardId = card.CardId, SetId = card.SetId, Quantity = card.Quantity });
            }
        }
        else
        {
            var toRemove = await _db.OwnedCards.Where(o => o.SetId == SetId).ToListAsync();
            _db.OwnedCards.RemoveRange(toRemove);
            foreach (var card in targets)
                card.Quantity = 0;
        }

        await _db.SaveChangesAsync();
        RefreshCompletion();
    }
}
