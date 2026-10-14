using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class SetDetailPage : ContentPage
{
    private readonly SetDetailPageViewModel _vm;

    public SetDetailPage(SetDetailPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.Cards.Count == 0)
            await _vm.LoadAsync();
    }

    private async void OnCardImageTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: CardItem card })
            await card.OpenHdCommand.ExecuteAsync(null);
    }

    private async void OnTagPillTapped(object sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: CardItem card }) return;

        if (!_vm.HasTags)
        {
            await DisplayAlert("No tags yet", "Create a tag first from the Tags section (burger menu).", "OK");
            return;
        }

        var options = _vm.Tags.Select(t => t.Name).Prepend("No tag").ToArray();
        var choice = await DisplayActionSheet("Set tag", "Cancel", null, options);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        card.SelectedTag = choice == "No tag" ? null : _vm.Tags.FirstOrDefault(t => t.Name == choice);
    }
}
