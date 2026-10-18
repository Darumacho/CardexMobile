using CommunityToolkit.Mvvm.ComponentModel;

namespace CardexMobile.ViewModels;

// Une ligne de la section "Languages" dans Settings — pendant mobile de l'équivalent desktop.
// L'anglais est verrouillé (toujours suivi, données historiques implicitement en anglais).
public partial class LanguageOptionViewModel : ObservableObject
{
    public string Code { get; }
    public string Label { get; }
    public bool IsLocked { get; }
    public bool IsEnabled => !IsLocked;

    [ObservableProperty] private bool _isTracked;

    public LanguageOptionViewModel(string code, string label, bool isTracked, bool isLocked = false)
    {
        Code = code;
        Label = label;
        _isTracked = isTracked;
        IsLocked = isLocked;
    }
}
