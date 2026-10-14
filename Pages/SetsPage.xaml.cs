using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class SetsPage : ContentPage
{
    private readonly SetsPageViewModel _vm;

    public SetsPage(SetsPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.Groups.Count == 0)
            await _vm.LoadAsync();
        else
            await _vm.RefreshCountsAsync();
    }

    private async void OnSetItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: SetItem set })
            await Shell.Current.GoToAsync($"{nameof(SetDetailPage)}?setId={set.SetId}&setName={Uri.EscapeDataString(set.Name)}");
    }
}
