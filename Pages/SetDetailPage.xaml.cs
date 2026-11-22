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
        vm.ScrollToCardRequested += OnScrollToCardRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.Cards.Count == 0)
            await _vm.LoadAsync();
        else
            // Resynchronise les compteurs après un retour de LanguageBreakdownPage (qui modifie
            // directement la CardItem partagée sans passer par SetDetailPageViewModel) — pas
            // besoin d'un rechargement complet puisque Cards est déjà peuplé.
            _vm.RefreshCompletion();
    }

    // Venant de Search avec une carte ciblée (voir SetDetailPageViewModel.LoadAsync) — scrolle
    // jusqu'à elle dans la vue actuellement visible (liste ou classeur) et surligne brièvement.
    private async void OnScrollToCardRequested(object? sender, CardItem card)
    {
        // L'event est levé juste après OnPropertyChanged(FilteredCards) côté ViewModel — la
        // CollectionView native n'a pas encore forcément fini de traiter ce nouvel ItemsSource à cet
        // instant précis (changement de binding → mise à jour de l'adapter natif, pas synchrone).
        // Sans ce délai, ScrollTo s'exécute sur une liste encore vide côté natif et ne fait rien.
        await Task.Delay(250);

        var view = _vm.IsListView ? CardsListView : CardsGridView;
        view.ScrollTo(card, position: ScrollToPosition.Center, animate: true);

        await Task.Delay(4000);
        card.IsHighlighted = false;
    }

    private async void OnCardImageTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: CardItem card })
            await card.OpenHdCommand.ExecuteAsync(null);
    }

    private async void OnLanguageBreakdownTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: CardItem card })
            await SetDetailPageViewModel.NavigateToLanguageBreakdownAsync(card);
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
