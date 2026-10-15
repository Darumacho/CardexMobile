using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsPageViewModel _vm;

    public SettingsPage(SettingsPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.CheckForUpdateAsync();
    }

    private void OnSwatchTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: ColorSwatch swatch })
            _vm.SetBorderColorCommand.Execute(swatch.Hex);
    }
}
