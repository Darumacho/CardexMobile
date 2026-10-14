using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class TagsPage : ContentPage
{
    private readonly TagsPageViewModel _vm;

    public TagsPage(TagsPageViewModel vm)
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

    private async void OnPendingColorTapped(object sender, TappedEventArgs e)
    {
        var hex = await PickColorAsync();
        if (hex is not null) _vm.SelectPendingColorCommand.Execute(hex);
    }

    private async void OnTagSwatchTapped(object sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: TagItem tag }) return;
        var hex = await PickColorAsync();
        if (hex is not null) tag.SetColorCommand.Execute(hex);
    }

    private async Task<string?> PickColorAsync()
    {
        var names = ColorSwatch.Palette.Select(c => c.Name).ToArray();
        var choice = await DisplayActionSheet("Pick a color", "Cancel", null, names);
        return ColorSwatch.Palette.FirstOrDefault(c => c.Name == choice)?.Hex;
    }

    private async void OnSaveTagClicked(object sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: TagItem tag })
            await _vm.UpdateTagCommand.ExecuteAsync(tag);
    }

    private async void OnDeleteTagClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: TagItem tag }) return;
        var confirm = await DisplayAlert("Delete tag", $"Delete the tag \"{tag.Name}\"? Cards tagged with it will lose this tag.", "Delete", "Cancel");
        if (confirm)
            await _vm.DeleteTagCommand.ExecuteAsync(tag);
    }
}
