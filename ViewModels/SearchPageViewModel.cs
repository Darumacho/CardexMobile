using CommunityToolkit.Mvvm.ComponentModel;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace CardexMobile.ViewModels;

// Recherche globale — même logique que MainViewModel.OnGlobalSearchChanged/RunSearchAsync côté
// desktop : debounce 300ms, recherche par nom OU numéro sur CachedCards, top 100 résultats.
public partial class SearchPageViewModel : ObservableObject
{
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;
    private CancellationTokenSource? _searchCts;

    public ObservableCollection<CardListItem> Results { get; } = [];

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    private string _searchText = "";

    public bool HasNoResults => Results.Count == 0 && !string.IsNullOrWhiteSpace(SearchText) && !IsBusy;

    public SearchPageViewModel(MobileAppDbContext db, DbInitializer dbInit)
    {
        _db = db;
        _dbInit = dbInit;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = Task.Delay(300, token).ContinueWith(
            _ => RunSearchAsync(value, token),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task RunSearchAsync(string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            OnPropertyChanged(nameof(HasNoResults));
            return;
        }

        IsBusy = true;
        try
        {
            await _dbInit.ReadyTask;
            if (token.IsCancellationRequested) return;

            var q = NormalizeStarDelta(query.Trim());
            var (namePart, numberPart) = SplitNameAndNumber(q);

            var queryable = namePart is not null
                // "regice 97", "charizard ex 105"... : dernier mot numérique isolé pour le numéro,
                // le reste pour le nom — sinon "regice 97" ne matchait ni Name ni Number en entier.
                ? _db.CachedCards.Where(c => EF.Functions.Like(c.Name, $"%{namePart}%") && c.Number == numberPart)
                : _db.CachedCards.Where(c => EF.Functions.Like(c.Name, $"%{q}%") || EF.Functions.Like(c.Number, $"%{q}%"));

            var cards = await queryable
                .OrderBy(c => c.Name)
                .Take(100)
                .ToListAsync(token);

            if (token.IsCancellationRequested) return;

            var setIds = cards.Select(c => c.SetId).Distinct().ToList();
            var setNames = await _db.CachedSets
                .Where(s => setIds.Contains(s.SetId))
                .ToDictionaryAsync(s => s.SetId, s => s.Name, token);

            if (token.IsCancellationRequested) return;

            var cardIds = cards.Select(c => c.CardId).ToList();
            var ownedQty = await _db.OwnedCards
                .Where(o => cardIds.Contains(o.CardId))
                .ToDictionaryAsync(o => o.CardId, o => o.Quantity, token);

            if (token.IsCancellationRequested) return;

            Results.Clear();
            foreach (var c in cards)
                Results.Add(new CardListItem
                {
                    CardId = c.CardId, Name = c.Name, Number = c.Number,
                    SetId = c.SetId, SetName = setNames.GetValueOrDefault(c.SetId, c.SetId),
                    ImageUrl = c.ImageSmall, Rarity = c.Rarity,
                    OwnedQuantity = ownedQty.GetValueOrDefault(c.CardId)
                });
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoResults));
        }
    }

    // "star"/"delta" en mot entier -> symboles Unicode littéraux du catalogue (★ Gold Star, δ Delta
    // Species — voir CardScanService.RuleBoxSuffixes, 177 cartes concernées). \b évite "staraptor"
    // (pas de frontière de mot après "star", collé à "aptor").
    private static readonly Regex StarWordPattern = new(@"\bstar\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DeltaWordPattern = new(@"\bdelta\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string NormalizeStarDelta(string query) =>
        DeltaWordPattern.Replace(StarWordPattern.Replace(query, "★"), "δ");

    // Isole un numéro de collection en fin de requête ("regice 97" -> ("regice", "97")) pour
    // permettre une recherche nom+numéro combinée — sans ça, "regice 97" ne matchait ni Name
    // ("Regice") ni Number ("97") en entier puisque LIKE compare la requête complète. Ne s'applique
    // que si un mot précède le nombre (sinon une recherche numéro seul, déjà gérée par le OR).
    private static (string? NamePart, string? NumberPart) SplitNameAndNumber(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !tokens[^1].All(char.IsDigit))
            return (null, null);
        return (string.Join(' ', tokens[..^1]), tokens[^1]);
    }
}
