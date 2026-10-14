using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Services;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

// Équivalent de AchievementViewModel desktop — enveloppe une définition + son état débloqué (ou
// non) pour l'affichage. Toujours construit pour les 35 définitions (verrouillées incluses), même
// logique que SettingsWindow.xaml desktop.
public class AchievementItem
{
    public AchievementDef Def { get; }
    public bool IsUnlocked { get; }
    public DateTime? UnlockedAt { get; }

    public string Emoji       => Def.Emoji;
    public string Name        => Def.Name;
    public double RowOpacity  => IsUnlocked ? 1.0 : 0.45;

    // Icône PNG reprise du desktop si disponible (Resources/Images/*.png), sinon repli emoji —
    // même logique que AchievementViewModel.HasIcon côté desktop.
    public bool HasIcon    => !string.IsNullOrEmpty(Def.Icon);
    public bool HasNoIcon  => !HasIcon;
    public string? IconSource => Def.Icon;

    // Masque la description tant que le succès "secret" n'est pas débloqué — même règle que
    // AchievementViewModel.DisplayDescription côté desktop.
    public string DisplayDescription => IsUnlocked || !Def.IsSecret ? Def.Description : "???";
    public string StatusText => IsUnlocked ? UnlockedAt!.Value.ToLocalTime().ToString("dd/MM/yyyy") : "🔒";

    public AchievementItem(AchievementDef def, DateTime? unlockedAt)
    {
        Def = def;
        UnlockedAt = unlockedAt;
        IsUnlocked = unlockedAt.HasValue;
    }
}

public partial class AchievementsPageViewModel : ObservableObject
{
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;

    public ObservableCollection<AchievementItem> Items { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";

    public AchievementsPageViewModel(MobileAppDbContext db, DbInitializer dbInit)
    {
        _db = db;
        _dbInit = dbInit;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _dbInit.ReadyTask;
            var unlocked = await _db.UnlockedAchievements.ToDictionaryAsync(a => a.Id, a => a.UnlockedAt);

            Items.Clear();
            foreach (var def in AchievementService.All)
                Items.Add(new AchievementItem(def, unlocked.TryGetValue(def.Id, out var at) ? at : null));

            ProgressText = $"{unlocked.Count} / {AchievementService.All.Count} unlocked";
        }
        finally { IsBusy = false; }
    }
}
