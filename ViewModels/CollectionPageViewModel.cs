using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Models;
using Cardex.Services;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

// Équivalent de la page d'accueil desktop (MainViewModel : HomeFavorites/HomeCollection,
// LoadWantedCardsAsync, LoadDuplicateCardsAsync) — 4 sections, rechargées à chaque apparition de
// la page puisqu'elles reflètent l'état courant de la collection.
public partial class CollectionPageViewModel : ObservableObject
{
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;
    private readonly AppSettings _settings;

    public ObservableCollection<SetItem> Favorites { get; } = [];
    public ObservableCollection<SetItem> MyCollection { get; } = [];
    public ObservableCollection<CardListItem> Wanted { get; } = [];
    public ObservableCollection<CardListItem> Duplicates { get; } = [];
    public ObservableCollection<TagSectionItem> TagSections { get; } = [];

    [ObservableProperty] private bool _isBusy;

    public bool HasFavorites    => Favorites.Count > 0;
    public bool HasMyCollection => _settings.ShowMyCollection && MyCollection.Count > 0;
    public bool HasWanted       => Wanted.Count > 0;
    public bool HasDuplicates   => Duplicates.Count > 0;
    public bool HasTagSections  => TagSections.Count > 0;
    public bool HasNothing      => !HasFavorites && !HasMyCollection && !HasWanted && !HasDuplicates && !HasTagSections && !IsBusy;

    // Blocs dépliables/repliables — état persisté via ExpandedStateStore (Preferences), restauré
    // tel quel au prochain lancement de l'app.
    [ObservableProperty] private bool _favoritesExpanded;
    [ObservableProperty] private bool _myCollectionExpanded;
    [ObservableProperty] private bool _wantedExpanded;
    [ObservableProperty] private bool _duplicatesExpanded;

    public string FavoritesChevron    => FavoritesExpanded    ? "▾" : "▸";
    public string MyCollectionChevron => MyCollectionExpanded ? "▾" : "▸";
    public string WantedChevron       => WantedExpanded       ? "▾" : "▸";
    public string DuplicatesChevron   => DuplicatesExpanded   ? "▾" : "▸";

    partial void OnFavoritesExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoritesChevron));
        ExpandedStateStore.Set("collection.favorites", value);
    }

    partial void OnMyCollectionExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(MyCollectionChevron));
        ExpandedStateStore.Set("collection.myCollection", value);
    }

    partial void OnWantedExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(WantedChevron));
        ExpandedStateStore.Set("collection.wanted", value);
    }

    partial void OnDuplicatesExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(DuplicatesChevron));
        ExpandedStateStore.Set("collection.duplicates", value);
    }

    [RelayCommand] private void ToggleFavoritesExpanded()    => FavoritesExpanded    = !FavoritesExpanded;
    [RelayCommand] private void ToggleMyCollectionExpanded() => MyCollectionExpanded = !MyCollectionExpanded;
    [RelayCommand] private void ToggleWantedExpanded()       => WantedExpanded       = !WantedExpanded;
    [RelayCommand] private void ToggleDuplicatesExpanded()   => DuplicatesExpanded   = !DuplicatesExpanded;

    public CollectionPageViewModel(MobileAppDbContext db, DbInitializer dbInit, AppSettings settings)
    {
        _db = db;
        _dbInit = dbInit;
        _settings = settings;
        _favoritesExpanded    = ExpandedStateStore.Get("collection.favorites");
        _myCollectionExpanded = ExpandedStateStore.Get("collection.myCollection");
        _wantedExpanded       = ExpandedStateStore.Get("collection.wanted");
        _duplicatesExpanded   = ExpandedStateStore.Get("collection.duplicates");
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _dbInit.ReadyTask;

            var ownedBySet = await _db.OwnedCards
                .Where(o => o.Quantity > 0)
                .GroupBy(o => o.SetId)
                .Select(g => new { SetId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SetId, x => x.Count);

            var favoriteIds = (await _db.FavoriteSets.Select(f => f.SetId).ToListAsync()).ToHashSet();

            var relevantSetIds = favoriteIds.Union(ownedBySet.Keys).ToList();
            var sets = await _db.CachedSets
                .Where(s => relevantSetIds.Contains(s.SetId))
                .OrderBy(s => s.ReleaseDate)
                .ToListAsync();

            Favorites.Clear();
            MyCollection.Clear();
            foreach (var s in sets)
            {
                var item = new SetItem
                {
                    SetId = s.SetId, Name = s.Name, LogoUrl = s.LogoUrl, Total = s.Total,
                    OwnedCount = ownedBySet.GetValueOrDefault(s.SetId),
                    BorderColorHex = _settings.CollectionBorderColor
                };
                if (favoriteIds.Contains(s.SetId)) Favorites.Add(item);
                if (ownedBySet.ContainsKey(s.SetId)) MyCollection.Add(item);
            }

            await LoadWantedAsync();
            await LoadDuplicatesAsync();
            await LoadTagSectionsAsync();
        }
        finally
        {
            IsBusy = false;
            NotifySectionsChanged();
        }
    }

    private async Task LoadWantedAsync()
    {
        var wantedIds = (await _db.WantedCards.Select(w => w.CardId).ToListAsync()).ToHashSet();
        Wanted.Clear();
        if (wantedIds.Count == 0) return;

        var cards = await _db.CachedCards
            .Where(c => wantedIds.Contains(c.CardId))
            .OrderBy(c => c.SetId).ThenBy(c => c.SortOrder)
            .ToListAsync();

        var setNames = await SetNamesForAsync(cards.Select(c => c.SetId));
        var ownedQty = await _db.OwnedCards
            .Where(o => wantedIds.Contains(o.CardId))
            .ToDictionaryAsync(o => o.CardId, o => o.Quantity);

        foreach (var c in cards)
            Wanted.Add(ToCardListItem(c, setNames, ownedQty.GetValueOrDefault(c.CardId)));
    }

    private async Task LoadDuplicatesAsync()
    {
        var dupes = await _db.OwnedCards.Where(o => o.Quantity > 1).ToListAsync();
        Duplicates.Clear();
        if (dupes.Count == 0) return;

        var cardIds = dupes.Select(o => o.CardId).ToList();
        var cards = await _db.CachedCards
            .Where(c => cardIds.Contains(c.CardId))
            .OrderBy(c => c.SetId).ThenBy(c => c.SortOrder)
            .ToListAsync();

        var setNames = await SetNamesForAsync(cards.Select(c => c.SetId));
        var qtyMap = dupes.ToDictionary(o => o.CardId, o => o.Quantity);

        foreach (var c in cards)
            Duplicates.Add(ToCardListItem(c, setNames, qtyMap.GetValueOrDefault(c.CardId)));
    }

    // Équivalent de MainViewModel.RefreshTagSectionsAsync côté desktop — groupe les cartes taguées
    // par tag, affiché sous Duplicates sur la page d'accueil (même ordre que MainWindow.xaml).
    private async Task LoadTagSectionsAsync()
    {
        var tags = await _db.Tags.OrderBy(t => t.Id).ToListAsync();
        TagSections.Clear();
        if (tags.Count == 0) return;

        var taggedCards = await _db.CardTags.ToListAsync();
        if (taggedCards.Count == 0) return;

        var cardIds = taggedCards.Select(ct => ct.CardId).ToList();
        var cards = await _db.CachedCards
            .Where(c => cardIds.Contains(c.CardId))
            .OrderBy(c => c.SetId).ThenBy(c => c.SortOrder)
            .ToListAsync();

        var setNames = await SetNamesForAsync(cards.Select(c => c.SetId));
        var ownedQty = await _db.OwnedCards
            .Where(o => cardIds.Contains(o.CardId))
            .ToDictionaryAsync(o => o.CardId, o => o.Quantity);

        foreach (var t in tags)
        {
            var tagCardIds = taggedCards
                .Where(ct => ct.TagId == t.Id)
                .Select(ct => ct.CardId)
                .ToHashSet();
            if (tagCardIds.Count == 0) continue;

            var section = new TagSectionItem(new TagItem { Id = t.Id, Name = t.Name, Color = t.Color });
            foreach (var c in cards.Where(c => tagCardIds.Contains(c.CardId)))
                section.Cards.Add(ToCardListItem(c, setNames, ownedQty.GetValueOrDefault(c.CardId)));
            TagSections.Add(section);
        }
    }

    private async Task<Dictionary<string, string>> SetNamesForAsync(IEnumerable<string> setIds)
    {
        var ids = setIds.Distinct().ToList();
        return await _db.CachedSets.Where(s => ids.Contains(s.SetId)).ToDictionaryAsync(s => s.SetId, s => s.Name);
    }

    private static CardListItem ToCardListItem(CachedCard c, Dictionary<string, string> setNames, int ownedQuantity) => new()
    {
        CardId = c.CardId, Name = c.Name, Number = c.Number,
        SetId = c.SetId, SetName = setNames.GetValueOrDefault(c.SetId, c.SetId),
        ImageUrl = c.ImageSmall, Rarity = c.Rarity, OwnedQuantity = ownedQuantity
    };

    private void NotifySectionsChanged()
    {
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasMyCollection));
        OnPropertyChanged(nameof(HasWanted));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(HasTagSections));
        OnPropertyChanged(nameof(HasNothing));
    }
}
