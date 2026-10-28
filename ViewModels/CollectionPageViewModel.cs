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
    private readonly PokemonTcgService _tcgService;
    private readonly PokeSpriteService _pokeSprite = new();

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

    [ObservableProperty] private CollectionStatsViewModel? _stats;
    public bool HasStats => _settings.ShowStats && Stats is not null;
    // Lu directement depuis _settings (pas d'[ObservableProperty]) : cette page est recréée
    // (AddTransient) à chaque navigation, donc la valeur est toujours fraîche sans notification
    // nécessaire — même raisonnement que HasStats ci-dessus.
    public bool StatsTeamMode => _settings.StatsTeamMode;
    public bool StatsSingleMode => !StatsTeamMode;
    public bool HasNoStats => !HasStats && !IsBusy;

    // Blocs dépliables/repliables — état persisté via ExpandedStateStore (Preferences), restauré
    // tel quel au prochain lancement de l'app.
    [ObservableProperty] private bool _favoritesExpanded;
    [ObservableProperty] private bool _myCollectionExpanded;
    [ObservableProperty] private bool _wantedExpanded;
    [ObservableProperty] private bool _duplicatesExpanded;
    [ObservableProperty] private bool _statsExpanded;

    public string FavoritesChevron    => FavoritesExpanded    ? "▾" : "▸";
    public string MyCollectionChevron => MyCollectionExpanded ? "▾" : "▸";
    public string WantedChevron       => WantedExpanded       ? "▾" : "▸";
    public string DuplicatesChevron   => DuplicatesExpanded   ? "▾" : "▸";
    public string StatsChevron        => StatsExpanded        ? "▾" : "▸";

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

    partial void OnStatsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatsChevron));
        ExpandedStateStore.Set("collection.stats", value);
    }

    [RelayCommand] private void ToggleFavoritesExpanded()    => FavoritesExpanded    = !FavoritesExpanded;
    [RelayCommand] private void ToggleMyCollectionExpanded() => MyCollectionExpanded = !MyCollectionExpanded;
    [RelayCommand] private void ToggleWantedExpanded()       => WantedExpanded       = !WantedExpanded;
    [RelayCommand] private void ToggleDuplicatesExpanded()   => DuplicatesExpanded   = !DuplicatesExpanded;
    [RelayCommand] private void ToggleStatsExpanded()        => StatsExpanded        = !StatsExpanded;

    public CollectionPageViewModel(MobileAppDbContext db, DbInitializer dbInit, AppSettings settings, PokemonTcgService tcgService)
    {
        _db = db;
        _dbInit = dbInit;
        _settings = settings;
        _tcgService = tcgService;
        _favoritesExpanded    = ExpandedStateStore.Get("collection.favorites");
        _myCollectionExpanded = ExpandedStateStore.Get("collection.myCollection");
        _wantedExpanded       = ExpandedStateStore.Get("collection.wanted");
        _duplicatesExpanded   = ExpandedStateStore.Get("collection.duplicates");
        _statsExpanded        = ExpandedStateStore.Get("collection.stats", defaultValue: false);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _dbInit.ReadyTask;
            await EnsureCachedCardsForOwnedSetsAsync();

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
            await LoadStatsAsync();
        }
        finally
        {
            IsBusy = false;
            NotifySectionsChanged();
        }
    }

    // Après une synchro depuis le desktop, OwnedCards peut référencer des cartes dont le catalogue
    // local (CachedCards) n'a jamais été chargé sur ce mobile — il ne se peuple que lazy, en visitant
    // SetDetailPage pour un set donné (voir SetDetailPageViewModel.LoadAsync). Tant qu'un set possédé
    // n'a pas été visité, ses cartes disparaissent SILENCIEUSEMENT des jointures ici (Stats, Wanted,
    // Duplicates, Tags) puisque ce sont des INNER JOIN — pas d'erreur, juste des cartes manquantes
    // dans les totaux (cause du bug "Lugia x10 jamais le plus collecté" : ses cartes n'étaient tout
    // simplement pas dans CachedCards). Rattrape une fois pour toutes les sets possédés manquants.
    private async Task EnsureCachedCardsForOwnedSetsAsync()
    {
        var ownedSetIds = await _db.OwnedCards.Where(o => o.Quantity > 0).Select(o => o.SetId).Distinct().ToListAsync();
        if (ownedSetIds.Count == 0) return;

        var cachedSetIds = (await _db.CachedCards.Select(c => c.SetId).Distinct().ToListAsync()).ToHashSet();
        var missingSetIds = ownedSetIds.Where(id => !cachedSetIds.Contains(id)).ToList();
        if (missingSetIds.Count == 0) return;

        foreach (var setId in missingSetIds)
        {
            try
            {
                // ReconcileAsync persiste déjà lui-même le fallback (voir son implémentation) — ne
                // pas le réajouter ici, sous peine de conflit de clé primaire au SaveChanges plus bas.
                var reconciled = await CompletionSeeder.ReconcileAsync(_db, setId, []);
                if (reconciled is not null) continue;

                var apiCards = await _tcgService.GetCardsAsync(setId);
                var order = 0;
                var newCached = apiCards.Select(c => new CachedCard
                {
                    CardId = c.Id, SetId = setId, Name = c.Name, Number = c.Number,
                    ImageSmall = c.Images.Small, ImageLarge = c.Images.Large,
                    Rarity = c.Rarity, SortOrder = order++, Supertype = c.Supertype,
                    CmLow  = c.Cardmarket?.Prices?.LowPrice,
                    TcgLow = c.Tcgplayer?.Prices?.Values.FirstOrDefault()?.Low,
                    CmUrl  = c.Cardmarket?.Url,
                    TcgUrl = c.Tcgplayer?.Url
                }).ToList();
                _db.CachedCards.AddRange(newCached);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // best-effort : set indisponible/API down ou conflit ponctuel, retenté au prochain
                // chargement — ne doit pas faire perdre le rattrapage des autres sets. Clear
                // indispensable : sans ça, les entités de CE set restent trackées "Added" malgré
                // l'échec et referaient échouer le SaveChanges du PROCHAIN set traité.
                _db.ChangeTracker.Clear();
            }
        }
    }

    // Pendant mobile de MainViewModel.RefreshStatsAsync desktop. Pas d'arbre Series en mémoire ici
    // (contrairement au desktop) : tout est recalculé par requêtes directes sur MobileAppDbContext.
    // Mobile n'a pas de notion d'"exclusion" sur la complétion d'un set (SetItem.CompletionText
    // compare déjà Owned/Total bruts, sans retirer les cartes exclues) — même simplicité conservée
    // ici pour SetsBegun/SetsComplete.
    private async Task LoadStatsAsync()
    {
        if (!_settings.ShowStats)
        {
            Stats = null;
            return;
        }

        var allSets = await _db.CachedSets.Select(s => new { s.SetId, s.Series, s.Total }).ToListAsync();
        var ownedCountsBySet = await _db.OwnedCards
            .Where(o => o.Quantity > 0)
            .GroupBy(o => o.SetId)
            .Select(g => new { SetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SetId, x => x.Count);

        var setsBegun = allSets.Count(s => ownedCountsBySet.ContainsKey(s.SetId));
        var setsComplete = allSets.Count(s => s.Total > 0 && ownedCountsBySet.GetValueOrDefault(s.SetId) >= s.Total);

        var ownedRows = await _db.OwnedCards
            .Where(o => o.Quantity > 0)
            .Join(_db.CachedCards, o => o.CardId, c => c.CardId, (o, c) => new { o.Quantity, o.IsHolo, c.Name, c.Rarity, c.Supertype })
            .ToListAsync();

        var totalOwned = ownedRows.Count;
        var totalHolo = ownedRows.Count(r => r.IsHolo);

        // Regroupe par nom d'espèce nettoyé des décorations TCG (voir PokeSpriteService.
        // CleanSpeciesName) : sans ça, "Lugia", "Shining Lugia", "Lugia V", "Lugia VSTAR"... sont
        // comptés séparément et aucune variante individuelle ne ressort comme "la plus collectée".
        // Filtré sur Supertype "Pokémon" : sinon Energy/Trainer faussaient le classement.
        var topPokemonRanked = ownedRows
            .Where(r => r.Supertype == "Pokémon")
            .GroupBy(r => PokeSpriteService.CleanSpeciesName(r.Name))
            .Select(g => new { Name = g.Key, Count = g.Sum(r => r.Quantity) })
            .OrderByDescending(g => g.Count)
            .ToList();
        var topPokemon = topPokemonRanked.FirstOrDefault();

        var rarityBreakdown = ownedRows
            .GroupBy(r => string.IsNullOrEmpty(r.Rarity) ? "Unknown" : r.Rarity!)
            .Select(g => new { Rarity = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Select(g => new RarityStat(g.Rarity, g.Count,
                totalOwned > 0 ? $"{g.Count * 100.0 / totalOwned:F1}%" : "0%"))
            .ToList();

        var topCategories = allSets
            .GroupBy(s => s.Series)
            .Select(g => new { Series = g.Key, Owned = g.Sum(s => ownedCountsBySet.GetValueOrDefault(s.SetId)), Total = g.Sum(s => s.Total) })
            .Where(x => x.Total > 0)
            .OrderByDescending(x => (double)x.Owned / x.Total)
            .ThenByDescending(x => x.Owned)
            .Take(3)
            .Select(x => new SeriesCompletionStat(x.Series, $"{x.Owned * 100.0 / x.Total:F0}%", x.Owned, x.Total))
            .ToList();

        // Best-effort : nom non reconnu par PokeAPI ou API indisponible -> pas de sprite, l'UI
        // masque simplement l'image dans ce cas.
        string? spriteUrl = topPokemon is not null ? await _pokeSprite.GetSpriteUrlAsync(topPokemon.Name) : null;

        // Mode "équipe" : les 6 premiers, avec le sprite du premier déjà récupéré ci-dessus pour
        // éviter un fetch en double.
        var topPokemonTeam = new List<PokemonTeamMember>();
        foreach (var entry in topPokemonRanked.Take(6))
        {
            var url = entry == topPokemon ? spriteUrl : await _pokeSprite.GetSpriteUrlAsync(entry.Name);
            topPokemonTeam.Add(new PokemonTeamMember { Name = entry.Name, Count = entry.Count, SpriteUrl = url });
        }

        Stats = new CollectionStatsViewModel
        {
            TotalOwnedCards = totalOwned,
            TotalHoloCards = totalHolo,
            TotalSets = allSets.Count,
            SetsBegun = setsBegun,
            SetsComplete = setsComplete,
            TopPokemonName = topPokemon?.Name,
            TopPokemonCount = topPokemon?.Count ?? 0,
            TopPokemonSpriteUrl = spriteUrl,
            TopPokemonTeam = topPokemonTeam,
            RarityBreakdown = rarityBreakdown,
            TopCategories = topCategories,
            UnlockedAchievements = await _db.UnlockedAchievements.CountAsync(),
            TotalAchievements = AchievementService.All.Count,
        };
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
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(HasNoStats));
    }
}
