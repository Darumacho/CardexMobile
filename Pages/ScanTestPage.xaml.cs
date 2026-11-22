using CardexMobile.ViewModels;

namespace CardexMobile.Pages;

public partial class ScanTestPage : ContentPage
{
    private readonly ScanTestPageViewModel _vm;

    public ScanTestPage(ScanTestPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        vm.CameraView = CameraViewControl;
    }

    // Coupe la caméra en quittant la page — sinon elle resterait allumée (et le timer de scan actif)
    // même après avoir quitté l'écran.
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_vm.IsLiveModeActive)
            _vm.ToggleLiveModeCommand.Execute(null);
    }
}
