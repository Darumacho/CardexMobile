using CommunityToolkit.Mvvm.ComponentModel;

namespace CardexMobile.ViewModels;

// Ligne de carte en lecture seule pour les listes plates (Search/Wanted/Duplicates) — même rôle
// que SearchResultViewModel côté desktop. Pas de commandes de toggle : le tap navigue vers le set.
public partial class CardListItem : ObservableObject
{
    public string CardId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Number { get; init; } = "";
    public string SetId { get; init; } = "";
    public string SetName { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string? Rarity { get; init; }

    // Mutable (pas { get; init; }) : le scan live peut incrémenter la possession directement depuis
    // la ligne de résultat (bouton "+1"), sans recharger toute la liste.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwned))]
    private int _ownedQuantity;

    public decimal? CmLow { get; init; }
    public decimal? TcgLow { get; init; }

    // Icône Pokémon locale (Card Scan uniquement) — résolue en best-effort après coup par
    // ScanTestPageViewModel (voir LocalPokemonSpriteCache), jamais au moment de la construction de
    // l'item pour ne pas ralentir l'affichage des résultats. Null partout ailleurs dans l'app.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSprite))]
    private ImageSource? _spriteSource;

    public bool HasRarity => Rarity is not null;
    public bool HasSprite => SpriteSource is not null;
    public bool HasOwned  => OwnedQuantity > 0;
    public string CmText  => CmLow.HasValue  ? $"€{CmLow.Value:F2}"  : "—";
    public string TcgText => TcgLow.HasValue ? $"${TcgLow.Value:F2}" : "—";
}
