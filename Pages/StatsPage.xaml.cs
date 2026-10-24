using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class StatsPage : ContentPage
{
    private readonly CollectionPageViewModel _vm;

    public StatsPage(CollectionPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
