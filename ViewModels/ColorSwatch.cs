namespace CardexMobile.ViewModels;

public class ColorSwatch
{
    public string Hex  { get; init; } = "";
    public string Name { get; init; } = "";
    public Microsoft.Maui.Graphics.Color Color => Microsoft.Maui.Graphics.Color.FromArgb(Hex);

    // Même palette que SettingsWindow desktop (SwatchBtnStyle) — réutilisée pour la couleur de
    // bordure de collection et pour les tags.
    public static readonly IReadOnlyList<ColorSwatch> Palette =
    [
        new() { Hex = "#3a7fc1", Name = "Sky Blue" },
        new() { Hex = "#1565c0", Name = "Navy Blue" },
        new() { Hex = "#3f51b5", Name = "Indigo" },
        new() { Hex = "#00bcd4", Name = "Cyan" },
        new() { Hex = "#4ecdc4", Name = "Teal" },
        new() { Hex = "#27ae60", Name = "Green" },
        new() { Hex = "#8bc34a", Name = "Lime" },
        new() { Hex = "#f1c40f", Name = "Yellow" },
        new() { Hex = "#e67e22", Name = "Orange" },
        new() { Hex = "#e74c3c", Name = "Red" },
        new() { Hex = "#e91e8c", Name = "Pink" },
        new() { Hex = "#8e44ad", Name = "Purple" },
        new() { Hex = "#95a5a6", Name = "Gray" },
        new() { Hex = "#ecf0f1", Name = "White" },
    ];
}
