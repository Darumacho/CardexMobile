using CommunityToolkit.Mvvm.ComponentModel;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

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

            var q = query.Trim();
            var cards = await _db.CachedCards
                .Where(c => EF.Functions.Like(c.Name, $"%{q}%") || EF.Functions.Like(c.Number, $"%{q}%"))
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
}
