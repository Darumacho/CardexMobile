using Cardex.Services;
using CommunityToolkit.Maui.Alerts;

namespace CardexMobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Toast d'unlock — abonnement unique au démarrage, équivalent du toast WPF animé côté
		// desktop (AchievementService.Unlocked). Pas de son ni d'icône PNG en v1 mobile, juste
		// emoji + texte via CommunityToolkit.Maui.Alerts.Toast.
		AchievementService.Unlocked += def =>
		{
			MainThread.BeginInvokeOnMainThread(() =>
				_ = Toast.Make($"{def.Emoji} {def.Name} — {def.Description}").Show());
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
