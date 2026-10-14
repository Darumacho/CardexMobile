using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchPageViewModel _vm;

    public SearchPage(SearchPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private async void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CardListItem item)
        {
            ResultsView.SelectedItem = null;
            await Shell.Current.GoToAsync($"{nameof(SetDetailPage)}?setId={item.SetId}&setName={Uri.EscapeDataString(item.SetName)}");
        }
    }
}
