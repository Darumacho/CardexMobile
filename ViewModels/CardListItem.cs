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
    public int OwnedQuantity { get; init; }

    public bool HasRarity => Rarity is not null;
    public bool HasOwned  => OwnedQuantity > 0;
}
