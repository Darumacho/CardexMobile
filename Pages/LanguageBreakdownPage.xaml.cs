using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class LanguageBreakdownPage : ContentPage
{
    public LanguageBreakdownPage(LanguageBreakdownPageViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
