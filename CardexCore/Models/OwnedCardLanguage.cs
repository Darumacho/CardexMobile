namespace Cardex.Models;

// Détail par langue des exemplaires possédés d'une carte, utilisé uniquement quand plus d'une
// langue est suivie (voir AppSettings.TrackedLanguages). OwnedCard.Quantity reste la source de
// vérité agrégée (somme de toutes les langues) pour tout le reste de l'app (complétion,
// achievements, backup/sync) — cette table est purement additive. Copie de l'équivalent desktop
// (Cardwatcher/Models/OwnedCardLanguage.cs) — ce projet reste indépendant du repo desktop.
public class OwnedCardLanguage
{
    public string CardId { get; set; } = "";
    public string SetId { get; set; } = "";
    public string Language { get; set; } = "EN";
    public int Quantity { get; set; } = 1;
}
