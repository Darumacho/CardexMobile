namespace CardexMobile.Pages;

[QueryProperty(nameof(ImageUrl), "imageUrl")]
[QueryProperty(nameof(CardName), "name")]
[QueryProperty(nameof(CardNumber), "number")]
[QueryProperty(nameof(CardRarity), "rarity")]
public partial class CardZoomPage : ContentPage
{
    public string ImageUrl
    {
        set => ZoomImage.Source = Uri.UnescapeDataString(value ?? "");
    }

    public string CardName
    {
        set => CardNameLabel.Text = Uri.UnescapeDataString(value ?? "");
    }

    public string CardNumber
    {
        set => UpdateDetails(number: Uri.UnescapeDataString(value ?? ""));
    }

    public string CardRarity
    {
        set => UpdateDetails(rarity: Uri.UnescapeDataString(value ?? ""));
    }

    private string _number = "";
    private string _rarity = "";

    public CardZoomPage()
    {
        InitializeComponent();
    }

    private void UpdateDetails(string? number = null, string? rarity = null)
    {
        if (number is not null) _number = number;
        if (rarity is not null) _rarity = rarity;
        CardDetailsLabel.Text = string.IsNullOrEmpty(_rarity) ? $"N° {_number}" : $"N° {_number}  ·  {_rarity}";
    }

    private async void OnBackgroundTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
