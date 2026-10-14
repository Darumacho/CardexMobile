using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class CollectionPage : ContentPage
{
    private readonly CollectionPageViewModel _vm;

    public CollectionPage(CollectionPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Contrairement à SetsPage, on recharge à chaque visite : ces 4 sections doivent refléter
        // l'état courant (un favori/want/doublon a pu changer depuis un autre onglet).
        await _vm.LoadAsync();
    }

    private async void OnSetItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: SetItem set })
            await Shell.Current.GoToAsync($"{nameof(SetDetailPage)}?setId={set.SetId}&setName={Uri.EscapeDataString(set.Name)}");
    }

    private async void OnCardItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: CardListItem item })
            await Shell.Current.GoToAsync($"{nameof(SetDetailPage)}?setId={item.SetId}&setName={Uri.EscapeDataString(item.SetName)}");
    }
}
