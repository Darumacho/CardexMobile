using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Models;
using Cardex.Services;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

public partial class SetItem : ObservableObject
{
    public string SetId   { get; init; } = "";
    public string Name    { get; init; } = "";
    public string LogoUrl { get; init; } = "";
    public int    Total   { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletionText))]
    [NotifyPropertyChangedFor(nameof(HasOwnedCards))]
    private int _ownedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderColor))]
    private string _borderColorHex = "#3a7fc1";

    public string CompletionText  => $"{OwnedCount} / {Total}";
    public bool   HasOwnedCards   => OwnedCount > 0;
    public Microsoft.Maui.Graphics.Color BorderColor => Microsoft.Maui.Graphics.Color.FromArgb(BorderColorHex);
}

// Bloc dépliable/repliable par série (Base, Gym, Neo…), rendu par un CollectionView groupé
// virtualisé (IsGrouped="True" sur SetsPage) — avec ~100-150 sets au total, un BindableLayout
// (non virtualisé, items tous réalisés d'un coup) a provoqué un ANR au premier repli d'un groupe.
// SeriesGroup EST directement la collection observable que CollectionView itère pour ce groupe :
// replier = Clear() (CollectionView libère les lignes), déplier = ré-Add() depuis AllSets (la
// liste complète, jamais vidée) — add/remove virtualisés plutôt qu'un simple IsVisible.
public class SeriesGroup : ObservableCollection<SetItem>
{
    public string Name { get; }
    public IReadOnlyList<SetItem> AllSets { get; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Chevron)));
            ExpandedStateStore.Set($"series.{Name}", value);
            if (_isExpanded) foreach (var s in AllSets) Add(s);
            else Clear();
        }
    }

    public string Chevron => IsExpanded ? "▾" : "▸";

    public Microsoft.Maui.Controls.Command ToggleExpandedCommand { get; }

    public SeriesGroup(string name, IReadOnlyList<SetItem> sets) : base(sets)
    {
        Name = name;
        AllSets = sets;
        ToggleExpandedCommand = new Microsoft.Maui.Controls.Command(() => IsExpanded = !IsExpanded);

        // Restaure l'état persisté sans repasser par le setter (pas d'écriture redondante dans le
        // store) ; si replié, vider ce que le constructeur de base a déjà ajouté depuis `sets`.
        _isExpanded = ExpandedStateStore.Get($"series.{name}");
        if (!_isExpanded) Clear();
    }
}

public partial class SetsPageViewModel : ObservableObject
{
    private readonly MobileAppDbContext _db;
    private readonly PokemonTcgService _tcgService;
    private readonly DbInitializer _dbInit;
    private readonly AppSettings _settings;

    public ObservableCollection<SeriesGroup> Groups { get; } = [];

    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private bool   _isRefreshing;

    // Évite d'afficher "No sets found" pendant le chargement initial (CollectionView.EmptyView
    // s'affiche dès que l'ItemsSource est vide, donc aussi le temps que le premier chargement
    // remplisse Groups) — le message ne doit apparaître qu'une fois le chargement terminé.
    public bool IsNotBusy => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    public SetsPageViewModel(MobileAppDbContext db, PokemonTcgService tcgService, DbInitializer dbInit, AppSettings settings)
    {
        _db = db;
        _tcgService = tcgService;
        _dbInit = dbInit;
        _settings = settings;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsRefreshing = true;
        try
        {
            await _dbInit.ReadyTask;
            try
            {
                var apiSets = await _tcgService.GetSetsAsync();
                var cachedIds = (await _db.CachedSets.Select(s => s.SetId).ToListAsync()).ToHashSet();
                var newSets = apiSets.Where(s => !cachedIds.Contains(s.Id)).ToList();
                if (newSets.Count > 0)
                {
                    _db.CachedSets.AddRange(newSets.Select(s => new CachedSet
                    {
                        SetId = s.Id, Name = s.Name, Series = s.Series, Total = s.Total,
                        ReleaseDate = s.ReleaseDate, LogoUrl = s.Images.Logo,
                        SymbolUrl = s.Images.Symbol, CachedAt = DateTime.UtcNow
                    }));
                    await _db.SaveChangesAsync();
                }
            }
            catch { }

            var sets = await _db.CachedSets.OrderBy(s => s.ReleaseDate).ToListAsync();
            var ownedBySet = await _db.OwnedCards
                .Where(o => o.Quantity > 0)
                .GroupBy(o => o.SetId)
                .Select(g => new { SetId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SetId, x => x.Count);

            Groups.Clear();
            foreach (var group in sets.GroupBy(s => s.Series).OrderBy(g => g.Min(s => s.ReleaseDate)))
            {
                var groupSets = group
                    .OrderBy(s => s.ReleaseDate)
                    .Select(s => new SetItem
                    {
                        SetId = s.SetId, Name = s.Name, LogoUrl = s.LogoUrl, Total = s.Total,
                        OwnedCount = ownedBySet.GetValueOrDefault(s.SetId),
                        BorderColorHex = _settings.CollectionBorderColor
                    }).ToList();
                Groups.Add(new SeriesGroup(group.Key, groupSets));
            }
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    public async Task RefreshCountsAsync()
    {
        var ownedBySet = await _db.OwnedCards
            .Where(o => o.Quantity > 0)
            .GroupBy(o => o.SetId)
            .Select(g => new { SetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SetId, x => x.Count);

        foreach (var group in Groups)
            foreach (var set in group.AllSets)
            {
                set.OwnedCount = ownedBySet.GetValueOrDefault(set.SetId);
                set.BorderColorHex = _settings.CollectionBorderColor;
            }
    }
}
