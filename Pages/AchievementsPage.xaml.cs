using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class AchievementsPage : ContentPage
{
    private readonly AchievementsPageViewModel _vm;

    public AchievementsPage(AchievementsPageViewModel vm)
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
