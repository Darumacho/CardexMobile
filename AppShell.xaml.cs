using CardexMobile.Pages;
using CardexMobile.ViewModels;

namespace CardexMobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(SetDetailPage), typeof(SetDetailPage));
        Routing.RegisterRoute(nameof(CardZoomPage), typeof(CardZoomPage));
    }

    // Action directe depuis le burger menu (pas de navigation vers une page) — réutilise
    // SettingsPageViewModel.SyncNowCommand (même logique d'avertissement/push que sur la page
    // Settings) plutôt que de dupliquer l'appel réseau ici. Résolu depuis le conteneur DI racine
    // car le Shell lui-même n'a pas de ViewModel/scope dédié.
    private async void OnSyncMenuItemClicked(object? sender, EventArgs e)
    {
        var vm = IPlatformApplication.Current?.Services.GetService<SettingsPageViewModel>();
        if (vm is null) return;
        await vm.SyncNowCommand.ExecuteAsync(null);
    }
}
