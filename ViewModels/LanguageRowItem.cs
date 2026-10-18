using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardexMobile.ViewModels;

// Une ligne de LanguageBreakdownPage — pendant mobile de LanguageRowViewModel côté desktop.
// Checked est dérivé de Quantity (pas de case séparée) : cocher/décocher la langue est un
// raccourci pour 1/0 exemplaire.
public partial class LanguageRowItem : ObservableObject
{
    public string Code { get; }
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Checked))]
    private int _quantity;

    public bool Checked => Quantity > 0;

    private readonly Func<string, int, Task> _onChanged;

    public LanguageRowItem(string code, string label, int quantity, Func<string, int, Task> onChanged)
    {
        Code = code;
        Label = label;
        _quantity = quantity;
        _onChanged = onChanged;
    }

    partial void OnQuantityChanged(int value) => _ = _onChanged(Code, value);

    [RelayCommand] private void Increment() => Quantity++;
    [RelayCommand] private void Decrement() => Quantity = Math.Max(0, Quantity - 1);
    [RelayCommand] private void Toggle() => Quantity = Quantity > 0 ? 0 : 1;
}
