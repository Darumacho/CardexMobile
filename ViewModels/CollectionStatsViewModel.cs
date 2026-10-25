namespace CardexMobile.ViewModels;

// Snapshot immuable recalculé à chaque chargement de CollectionPage — pas de notification de
// propriété individuelle, CollectionPageViewModel remplace l'instance entière et notifie juste
// "Stats". Pendant mobile de CollectionStatsViewModel desktop ; TopPokemonSpriteUrl est une simple
// URL (pas un BitmapImage) puisque le chargeur d'images MAUI gère déjà le cache disque/mémoire
// pour un <Image Source="..."> lié directement à une URL.
public class CollectionStatsViewModel
{
    public int TotalOwnedCards { get; init; }
    public int TotalHoloCards { get; init; }
    public int TotalSets { get; init; }
    public int SetsBegun { get; init; }
    public int SetsComplete { get; init; }
    public string? TopPokemonName { get; init; }
    public int TopPokemonCount { get; init; }
    public string? TopPokemonSpriteUrl { get; init; }
    public bool HasTopPokemonSprite => !string.IsNullOrEmpty(TopPokemonSpriteUrl);
    public List<RarityStat> RarityBreakdown { get; init; } = [];
    public List<SeriesCompletionStat> TopCategories { get; init; } = [];
    public int UnlockedAchievements { get; init; }
    public int TotalAchievements { get; init; }
}

public record RarityStat(string Rarity, int Count, string PercentText);
public record SeriesCompletionStat(string SeriesName, string PercentText, int Owned, int Total);
