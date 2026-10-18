using CommunityToolkit.Mvvm.ComponentModel;
using Cardex.Models;
using Cardex.Services;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

// Page modale ouverte depuis SetDetailPage quand plus d'une langue est suivie (voir
// SetDetailPageViewModel.IsMultiLanguageMode) — équivalent mobile du select par langue inline du
// desktop (Popup dans CardTemplate). Reçoit directement l'instance CardItem partagée avec
// SetDetailPageViewModel.Cards via la navigation (pas de query string) : les changements ici sont
// donc visibles immédiatement dans la liste au retour, sans recharger le set.
public partial class LanguageBreakdownPageViewModel : ObservableObject, IQueryAttributable
{
    private readonly MobileAppDbContext _db;
    private readonly AppSettings _settings;
    private CardItem? _card;

    [ObservableProperty] private string _cardName = "";
    [ObservableProperty] private string _cardSubtitle = "";

    public ObservableCollection<LanguageRowItem> Rows { get; } = [];

    public string TotalLabel
    {
        get
        {
            var total = _card?.Quantity ?? 0;
            return $"Total: {total} {(total == 1 ? "copy" : "copies")}";
        }
    }

    public LanguageBreakdownPageViewModel(MobileAppDbContext db, AppSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("card", out var c) && c is CardItem card)
        {
            _card = card;
            CardName = card.Name;
            CardSubtitle = card.Rarity is { } r ? $"#{card.Number} · {r}" : $"#{card.Number}";
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (_card is null) return;

        var existing = await _db.OwnedCardLanguages
            .Where(l => l.CardId == _card.CardId)
            .ToDictionaryAsync(l => l.Language, l => l.Quantity);

        // Filet de sécurité : si cette carte était déjà possédée avant le passage en mode
        // multi-langue et qu'aucune ligne de détail n'a encore été créée pour elle (page ouverte
        // sans passer par SetDetailPageViewModel.LoadAsync, ex. retour arrière sur une page déjà
        // chargée), on rattrape le même backfill plutôt que de perdre silencieusement le stock
        // existant (tout attribué à l'anglais, même hypothèse que côté desktop).
        if (existing.Count == 0 && _card.Quantity > 0)
        {
            existing["EN"] = _card.Quantity;
            _db.OwnedCardLanguages.Add(new OwnedCardLanguage { CardId = _card.CardId, SetId = _card.SetId, Language = "EN", Quantity = _card.Quantity });
            await _db.SaveChangesAsync();
        }

        Rows.Clear();
        foreach (var code in LanguageCatalog.All.Select(l => l.Code).Where(c => _settings.TrackedLanguages.Contains(c)))
        {
            var qty = existing.GetValueOrDefault(code);
            Rows.Add(new LanguageRowItem(code, LanguageCatalog.Label(code), qty, OnRowChangedAsync));
        }
        OnPropertyChanged(nameof(TotalLabel));
    }

    private async Task OnRowChangedAsync(string language, int quantity)
    {
        if (_card is null) return;

        var entry = await _db.OwnedCardLanguages.FindAsync(_card.CardId, language);
        if (quantity > 0)
        {
            if (entry is null)
                _db.OwnedCardLanguages.Add(new OwnedCardLanguage { CardId = _card.CardId, SetId = _card.SetId, Language = language, Quantity = quantity });
            else
                entry.Quantity = quantity;
        }
        else if (entry is not null)
        {
            _db.OwnedCardLanguages.Remove(entry);
        }
        await _db.SaveChangesAsync();

        var breakdown = await _db.OwnedCardLanguages.Where(l => l.CardId == _card.CardId).ToDictionaryAsync(l => l.Language, l => l.Quantity);
        var total = breakdown.Values.Sum();

        _card.LanguageQuantities = breakdown.Count > 0 ? breakdown : null;
        _card.Quantity = total;

        var ownedEntry = await _db.OwnedCards.FindAsync(_card.CardId);
        if (total > 0)
        {
            if (ownedEntry is null) _db.OwnedCards.Add(new OwnedCard { CardId = _card.CardId, SetId = _card.SetId, Quantity = total });
            else ownedEntry.Quantity = total;
        }
        else if (ownedEntry is not null)
        {
            _db.OwnedCards.Remove(ownedEntry);
        }
        await _db.SaveChangesAsync();
        await AchievementChecks.CheckOwnershipAsync(_db, total);

        OnPropertyChanged(nameof(TotalLabel));
    }
}
