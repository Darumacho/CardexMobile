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
    [NotifyPropertyChangedFor(nameof(LanguageSummary))]
    private int _quantity;

    // Détail par langue, utilisé seulement en affichage ici (le select/stepper vit sur
    // LanguageBreakdownPage) — voir SetDetailPageViewModel.IsMultiLanguageMode.
    public Dictionary<string, int>? LanguageQuantities { get; set; }

    public string LanguageSummary
    {
        get
        {
            if (Quantity == 0) return "Not owned";
            if (LanguageQuantities is null) return $"EN ×{Quantity}";
            var checkedLangs = LanguageQuantities.Where(kv => kv.Value > 0).ToList();
            if (checkedLangs.Count <= 3)
                return string.Join(", ", checkedLangs.Select(kv => $"{kv.Key} ×{kv.Value}"));
            return $"{checkedLangs.Count} languages · {Quantity} total";
        }
    }

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

    public void NotifyLanguageQuantitiesChanged() => OnPropertyChanged(nameof(LanguageSummary));

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

    // Les 4 sets "Trainer Kit" demi-deck (EX Latias/Latios, EX Plusle/Minun) partagent un même
    // code Cardmarket/TCGPlayer ("TK1"/"TK2") avec un suffixe a/b porté par le NUMÉRO de la carte,
    // pas par le set — vérifié empiriquement, "Bagon TK1A 1" ne donne aucun résultat, "Bagon TK1 1A"
    // si. Même correctif que CardViewModel.CmSearchQuery/TcgSearchQuery côté desktop.
    private static readonly Dictionary<string, (string Code, string Suffix)> MarketplaceOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        { "tk1a", ("TK1", "a") },
        { "tk1b", ("TK1", "b") },
        { "tk2a", ("TK2", "a") },
        { "tk2b", ("TK2", "b") },
    };

    // DP Trainer Kit : suffixe = initiale du Pokémon AVANT le numéro (ex. "L1"), pas de padding.
    // La plupart des autres (BW/XY) font l'inverse : NUMÉRO puis lettre (ex. "1S"). Exceptions
    // confirmées empiriquement par l'utilisateur au fil de plusieurs essais ("Cardmarket ne fait
    // rien de manière constante") : TK9 (Pikachu Libre/Suicune) repasse en lettre-avant-numéro
    // comme DP ; SM (TK10) est lettre-avant-numéro mais avec le numéro complété à 2 chiffres
    // seulement s'il est < 10 ("L01", "L10").
    // Les deux moitiés d'un même coffret partagent toujours le même numéro TK. Même table que
    // SetShortCodes.TrainerKitLetterOverrides desktop.
    private enum TrainerKitLetterPosition { BeforeNumber, BeforeNumberPadded, AfterNumber }

    private static readonly Dictionary<string, (string Code, string PokemonLetter, TrainerKitLetterPosition Position)> TrainerKitLetterOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        { "tk-dp-l",     ("TK3",  "L", TrainerKitLetterPosition.BeforeNumber) },       // DP Trainer Kit Lucario
        { "tk-dp-m",     ("TK3",  "M", TrainerKitLetterPosition.BeforeNumber) },       // DP Trainer Kit Manaphy
        { "tk-bw-z",     ("TK5",  "Z", TrainerKitLetterPosition.AfterNumber) },        // BW Trainer Kit Zoroark
        { "tk-bw-e",     ("TK5",  "E", TrainerKitLetterPosition.AfterNumber) },        // BW Trainer Kit Excadrill
        { "tk-xy-n",     ("TK6",  "N", TrainerKitLetterPosition.AfterNumber) },        // XY Trainer Kit Noivern
        { "tk-xy-sy",    ("TK6",  "S", TrainerKitLetterPosition.AfterNumber) },        // XY Trainer Kit Sylveon
        { "tk-xy-b",     ("TK7",  "B", TrainerKitLetterPosition.AfterNumber) },        // XY Trainer Kit Bisharp
        { "tk-xy-w",     ("TK7",  "W", TrainerKitLetterPosition.AfterNumber) },        // XY Trainer Kit Wigglytuff
        { "tk-xy-latia", ("TK8",  "a", TrainerKitLetterPosition.AfterNumber) },        // XY Trainer Kit Latias
        { "tk-xy-latio", ("TK8",  "o", TrainerKitLetterPosition.AfterNumber) },        // XY Trainer Kit Latios
        { "tk-xy-p",     ("TK9",  "P", TrainerKitLetterPosition.BeforeNumber) },       // XY Trainer Kit Pikachu Libre
        { "tk-xy-su",    ("TK9",  "S", TrainerKitLetterPosition.BeforeNumber) },       // XY Trainer Kit Suicune
        { "tk-sm-l",     ("TK10", "L", TrainerKitLetterPosition.BeforeNumberPadded) }, // SM Trainer Kit Lycanroc
        { "tk-sm-r",     ("TK10", "A", TrainerKitLetterPosition.BeforeNumberPadded) }, // SM Trainer Kit Alolan Raichu
    };

    private static string FormatTrainerKitNumber(string number, string letter, TrainerKitLetterPosition position) => position switch
    {
        TrainerKitLetterPosition.BeforeNumber => $"{letter}{number}",
        TrainerKitLetterPosition.BeforeNumberPadded => $"{letter}{(int.TryParse(number, out var n) && n < 10 ? n.ToString("D2") : number)}",
        _ => $"{number}{letter}",
    };

    // Cardmarket (uniquement — pas TCGPlayer) veut le code court plutôt que le SetId brut à
    // partir du bloc XY, contrairement aux blocs précédents (WotC à BW) qui fonctionnent avec le
    // SetId en majuscules. Vérifié empiriquement par l'utilisateur : "PFL" marche pour Phantasmal
    // Flames (me2), "ME2" non. Mobile n'a pas de table ShortCode par set (contrairement au
    // desktop) : copie directe des entrées XY-et-après de SetShortCodes.BySetId desktop.
    private static readonly Dictionary<string, string> CardmarketShortCodesFromXy = new(StringComparer.OrdinalIgnoreCase)
    {
        { "xy0", "KSS" }, { "xy1", "XY" }, { "xy2", "FF" }, { "mcd14", "MCD14" }, { "xy3", "FRF" },
        { "xy4", "PHF" }, { "xy5", "PC" }, { "dc1", "DCR" }, { "xy6", "ROS" }, { "xy7", "AO" },
        { "xy8", "BRK" }, { "mcd15", "MCD15" }, { "xy9", "BRP" }, { "g1", "GEN" }, { "xy10", "FC" },
        { "xy11", "STM" }, { "mcd16", "MCD16" }, { "xy12", "EVL" },
        { "smp", "SBP" }, { "sm1", "SM" }, { "sm2", "GRI" }, { "sm3", "BSH" }, { "sm35", "SHL" },
        { "sm4", "CINV" }, { "mcd17", "MCD17" }, { "sm5", "UPR" }, { "sm6", "FBL" }, { "sm7", "CST" },
        { "sm75", "DRJ" }, { "mcd18", "MCD18" }, { "sm8", "LTH" }, { "sm9", "TUP" }, { "det1", "DEP" },
        { "sm10", "UBN" }, { "sm11", "UMN" }, { "sm115", "HFT" }, { "sma", "HFSV" }, { "mcd19", "MCD19" },
        { "sm12", "CE" },
        { "swshp", "SWP" }, { "swsh1", "SWSH" }, { "swsh2", "RCL" }, { "swsh3", "DAB" },
        { "fut20", "FCL" }, { "swsh35", "CHP" }, { "swsh4", "VV" }, { "mcd21", "MCD21" },
        { "swsh45sv", "SFSV" }, { "swsh45", "SHF" }, { "swsh5", "BST" }, { "swsh6", "CHR" },
        { "swsh7", "EVS" }, { "cel25c", "CLSC" }, { "cel25", "CEL" }, { "swsh8", "FST" },
        { "swsh9", "BRS" }, { "swsh9tg", "BRTG" }, { "swsh10", "ASR" }, { "swsh10tg", "ARTG" },
        { "pgo", "PGO" }, { "mcd22", "MCD22" }, { "swsh11", "LOR" }, { "swsh11tg", "LOTG" },
        { "swsh12", "SVT" }, { "swsh12tg", "SVTG" }, { "svp", "SVP" }, { "swsh12pt5", "CZ" },
        { "swsh12pt5gg", "CZGG" },
        { "sv1", "SV1" }, { "sve", "SVE" }, { "sv2", "PAL" }, { "sv3", "OBF" }, { "sv3pt5", "MEW" },
        { "sv4", "PAR" }, { "sv4pt5", "PAF" }, { "sv5", "TEF" }, { "sv6", "TWM" }, { "sv6pt5", "SFA" },
        { "sv7", "SCR" }, { "sv8", "SSP" }, { "sv8pt5", "PRE" }, { "sv9", "JTG" }, { "sv10", "DRI" },
        { "zsv10pt5", "BLK" }, { "rsv10pt5", "WHT" },
        { "me1", "MEG" }, { "me2", "PFL" }, { "me2pt5", "ASC" }, { "me3", "POR" }, { "me4", "CRI" },
        { "me5", "PBL" },
        { "ex5.5", "PCCP" }, // Poké Card Creator Pack — exception pré-XY, "PCCP" marche, "EX5.5" non.

        // Blocs Base/Gym/Neo/E-Card, EX, Diamond & Pearl/Platinum, HGSS, Black & White — même
        // règle étendue à la demande de l'utilisateur. Exclus volontairement : dpp/hsp (override
        // dédié ci-dessous), bwp (confirmé correct tel quel avec le SetId brut), xyp (conflit non
        // résolu), et les sets Trainer Kit (règles dédiées plus haut).
        { "base1", "BS" }, { "base2", "JU" }, { "basep", "WBP" }, { "base3", "FO" },
        { "base4", "BS2" }, { "base5", "TR" }, { "gym1", "GH" }, { "gym2", "GC" },
        { "neo1", "NG" }, { "neo2", "NDI" }, { "si1", "SI" }, { "neo3", "NR" },
        { "neo4", "NDE" }, { "base6", "LC" }, { "ecard1", "EXP" }, { "bp", "BOG" },
        { "ecard2", "AQ" }, { "ecard3", "SK" },
        { "ex1", "RS" }, { "ex2", "SS" }, { "np", "NBP" }, { "ex3", "DR" }, { "ex4", "MA" },
        { "ex5", "HL" }, { "ex6", "FRLG" }, { "pop1", "P1" }, { "ex7", "TRR" }, { "ex8", "DX" },
        { "ex9", "EM" }, { "pop2", "P2" }, { "ex10", "UF" }, { "ex11", "DS" }, { "ex12", "LM" },
        { "pop3", "P3" }, { "ex13", "HP" }, { "pop4", "P4" }, { "ex14", "CG" }, { "ex15", "DF" },
        { "ex16", "PK" }, { "pop5", "P5" },
        { "dp1", "DP" }, { "dp2", "MT" }, { "pop6", "P6" }, { "dp3", "SW" }, { "dp4", "GE" },
        { "pop7", "P7" }, { "dp5", "MD" }, { "dp6", "LA" }, { "pop8", "P8" }, { "dp7", "STF" },
        { "pl1", "PLT" }, { "pop9", "P9" }, { "pl2", "RR" }, { "pl3", "SVC" }, { "pl4", "ARC" },
        { "ru1", "RMB" }, { "hgss1", "HGSS" }, { "hgss2", "UL" }, { "hgss3", "UD" },
        { "hgss4", "TM" }, { "col1", "CL" },
        { "bw1", "BW" }, { "mcd11", "MCD11" }, { "bw2", "EP" }, { "bw3", "NV" }, { "bw4", "ND" },
        { "bw5", "DE" }, { "mcd12", "MCD12" }, { "bw6", "DRX" }, { "dv1", "DRV" }, { "bw7", "BC" },
        { "bw8", "PST" }, { "bw9", "PFZ" }, { "bw10", "PLB" }, { "bw11", "LT" },
    };

    // Exceptions ponctuelles Cardmarket où ni le SetId brut, ni CardmarketShortCodesFromXy, ni les
    // règles Trainer Kit ne suffisent — vérifié empiriquement carte par carte. Code=null veut dire
    // "pas de code du tout dans la requête, juste le nom et le numéro" (HGSS Black Star Promos).
    // StripNumberPrefix retire un préfixe du NUMÉRO avant de l'insérer ("DP01" -> "01").
    private static readonly Dictionary<string, (string? Code, string? StripNumberPrefix)> CardmarketCustomOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        { "dpp", ("DPPR", "DP") }, // DP Black Star Promos : "DPPR 1" marche, "DPP DP1" non.
        { "hsp", (null,   null) }, // HGSS Black Star Promos : "HGSS1" seul marche, "HSP HGSS1" non.
    };

    private static string StripNumberPrefix(string number, string? prefix) =>
        prefix is not null && number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? number[prefix.Length..]
            : number;

    // Pas d'URL directe pour certains sets de secours (TCGdex) : on retombe sur une recherche par
    // nom + set + numéro, même format que CardViewModel.OpenCmLink/OpenTcgLink côté desktop.
    private string CmSearchQuery => Uri.EscapeDataString(
        CardmarketCustomOverrides.TryGetValue(SetId, out var cmCustom)
            ? (cmCustom.Code is null
                ? $"{Name} {StripNumberPrefix(Number, cmCustom.StripNumberPrefix)}"
                : $"{Name} {cmCustom.Code} {StripNumberPrefix(Number, cmCustom.StripNumberPrefix)}")
            : MarketplaceOverrides.TryGetValue(SetId, out var o)
                ? $"{Name} {o.Code} {Number}{o.Suffix}"
                : TrainerKitLetterOverrides.TryGetValue(SetId, out var lo)
                    ? $"{Name} {lo.Code} {FormatTrainerKitNumber(Number, lo.PokemonLetter, lo.Position)}"
                    : CardmarketShortCodesFromXy.TryGetValue(SetId, out var sc)
                        ? $"{Name} {sc} {Number}"
                        : $"{Name} {SetId.ToUpperInvariant()} {Number}");

    private string TcgSearchQuery => Uri.EscapeDataString(
        MarketplaceOverrides.TryGetValue(SetId, out var to)
            ? $"{Name} {to.Code} {Number}{to.Suffix}"
            : TrainerKitLetterOverrides.TryGetValue(SetId, out var tlo)
                ? $"{Name} {tlo.Code} {FormatTrainerKitNumber(Number, tlo.PokemonLetter, tlo.Position)}"
                : $"{Name} {SetId.ToUpperInvariant()} {Number}");

    [RelayCommand]
    private Task OpenCmLink()
    {
        var url = !string.IsNullOrEmpty(CmUrl) ? CmUrl
            : $"https://www.cardmarket.com/en/Pokemon/Products/Search?searchString={CmSearchQuery}";
        return Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(url);
    }

    [RelayCommand]
    private Task OpenTcgLink()
    {
        var url = !string.IsNullOrEmpty(TcgUrl) ? TcgUrl
            : $"https://www.tcgplayer.com/search/pokemon/product?productLineName=pokemon&view=grid&q={TcgSearchQuery}";
        return Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(url);
    }
}

public partial class SetDetailPageViewModel : ObservableObject, IQueryAttributable
{
    private readonly MobileAppDbContext _db;
    private readonly PokemonTcgService  _tcgService;
    private readonly DbInitializer      _dbInit;
    private readonly AppSettings        _settings;
    private string _setSeries = "";

    // Vrai quand plus d'une langue est suivie (Settings → Languages) : le compteur simple +/- est
    // remplacé par le bouton de résumé par langue (ouvre LanguageBreakdownPage). Même sémantique
    // que CardViewModel.IsMultiLanguageMode côté desktop.
    public bool IsMultiLanguageMode => _settings.TrackedLanguages.Count > 1;

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

    public SetDetailPageViewModel(MobileAppDbContext db, PokemonTcgService tcgService, DbInitializer dbInit, AppSettings settings)
    {
        _db = db;
        _tcgService = tcgService;
        _dbInit = dbInit;
        _settings = settings;
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
            var langMap  = await BuildLanguageMapAsync(cardIds, owned);

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
                    LanguageQuantities = langMap.GetValueOrDefault(c.CardId),
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

    // Construit (à l'ouverture du set) la map CardId -> quantité par langue depuis
    // OwnedCardLanguages. Si plus d'une langue est suivie, toute carte déjà possédée (owned) mais
    // sans détail par langue encore enregistré se voit attribuer rétroactivement tout son stock à
    // l'anglais (même logique que desktop MainViewModel.BuildLanguageMapAsync).
    private async Task<Dictionary<string, Dictionary<string, int>>> BuildLanguageMapAsync(
        List<string> cardIds, Dictionary<string, int> owned)
    {
        var langMap = new Dictionary<string, Dictionary<string, int>>();
        if (!IsMultiLanguageMode) return langMap;

        var rows = await _db.OwnedCardLanguages.Where(l => cardIds.Contains(l.CardId)).ToListAsync();
        foreach (var row in rows)
        {
            if (!langMap.TryGetValue(row.CardId, out var dict))
                langMap[row.CardId] = dict = new();
            dict[row.Language] = row.Quantity;
        }

        var toBackfill = owned.Where(kv => kv.Value > 0 && !langMap.ContainsKey(kv.Key)).ToList();
        if (toBackfill.Count > 0)
        {
            foreach (var kv in toBackfill)
            {
                langMap[kv.Key] = new() { ["EN"] = kv.Value };
                _db.OwnedCardLanguages.Add(new OwnedCardLanguage { CardId = kv.Key, SetId = SetId, Language = "EN", Quantity = kv.Value });
            }
            await _db.SaveChangesAsync();
        }

        return langMap;
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
    // le tap en vue binder. En mode multi-langue, bascule tout à zéro ou met 1 exemplaire en EN,
    // comme le raccourci "Owned" côté desktop.
    private async Task ToggleOwnedAsync(CardItem card)
    {
        if (IsMultiLanguageMode)
        {
            if (card.Quantity > 0)
                await SetLanguageQuantityAsync(card, null, 0);
            else
                await SetLanguageQuantityAsync(card, "EN", 1);
        }
        else
        {
            card.Quantity = card.Quantity > 0 ? 0 : 1;
            await SaveOwnedAsync(card);
        }
        RefreshCompletion();
    }

    // Écrit le détail par langue d'une carte (upsert une langue, ou remet tout à zéro si language
    // est null) puis resynchronise l'agrégat OwnedCard.Quantity à partir de la somme. Utilisé par
    // ToggleOwnedAsync (binder view) ; LanguageBreakdownPageViewModel a sa propre copie pour ne pas
    // dépendre de cette classe depuis une autre page.
    private async Task SetLanguageQuantityAsync(CardItem card, string? language, int quantity)
    {
        if (language is null)
        {
            var all = await _db.OwnedCardLanguages.Where(l => l.CardId == card.CardId).ToListAsync();
            _db.OwnedCardLanguages.RemoveRange(all);
        }
        else
        {
            var entry = await _db.OwnedCardLanguages.FindAsync(card.CardId, language);
            if (quantity > 0)
            {
                if (entry is null) _db.OwnedCardLanguages.Add(new OwnedCardLanguage { CardId = card.CardId, SetId = card.SetId, Language = language, Quantity = quantity });
                else entry.Quantity = quantity;
            }
            else if (entry is not null)
            {
                _db.OwnedCardLanguages.Remove(entry);
            }
        }
        await _db.SaveChangesAsync();

        var total = await _db.OwnedCardLanguages.Where(l => l.CardId == card.CardId).SumAsync(l => (int?)l.Quantity) ?? 0;
        card.LanguageQuantities = await _db.OwnedCardLanguages.Where(l => l.CardId == card.CardId).ToDictionaryAsync(l => l.Language, l => l.Quantity);
        card.Quantity = total;
        await SaveOwnedAsync(card);
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

    // Passe l'objet CardItem directement (pas de sérialisation en chaîne) : LanguageBreakdownPage
    // modifie la même instance partagée avec Cards, donc le retour sur cette page n'a besoin que
    // de RefreshCompletion() (voir SetDetailPage.xaml.cs.OnAppearing), pas d'un rechargement complet.
    public static Task NavigateToLanguageBreakdownAsync(CardItem card) =>
        Shell.Current.GoToAsync(nameof(CardexMobile.Pages.LanguageBreakdownPage),
            new Dictionary<string, object> { ["card"] = card });

    // Public pour que SetDetailPage.OnAppearing puisse resynchroniser les compteurs après un
    // retour de LanguageBreakdownPage, sans recharger tout le set (Cards.Count reste > 0).
    public void RefreshCompletion()
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

        if (IsMultiLanguageMode)
        {
            if (targetState)
            {
                var existingLangIds = (await _db.OwnedCardLanguages
                    .Where(l => l.SetId == SetId)
                    .Select(l => l.CardId)
                    .ToListAsync()).ToHashSet();
                foreach (var card in targets)
                    if (!existingLangIds.Contains(card.CardId))
                        _db.OwnedCardLanguages.Add(new OwnedCardLanguage { CardId = card.CardId, SetId = card.SetId, Language = "EN", Quantity = 1 });
            }
            else
            {
                var toRemoveLang = await _db.OwnedCardLanguages.Where(l => l.SetId == SetId).ToListAsync();
                _db.OwnedCardLanguages.RemoveRange(toRemoveLang);
            }
            foreach (var card in targets)
            {
                // Détail exact rechargé au prochain LoadAsync — resynchronisé ici pour que le
                // résumé affiché (LanguageSummary) ne mente pas avant ce rechargement.
                card.LanguageQuantities = card.Quantity > 0 ? new() { ["EN"] = card.Quantity } : null;
                card.NotifyLanguageQuantitiesChanged();
            }
        }

        await _db.SaveChangesAsync();
        RefreshCompletion();
    }
}
