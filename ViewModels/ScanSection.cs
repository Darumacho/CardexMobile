using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardexMobile.ViewModels;

// Une carte distincte confirmée pendant un scan live : son en-tête (nom+numéro au moment de la
// confirmation) et ses résultats candidats, repliable. Voir [[project_ocr_scan]] pour le
// fonctionnement du scan en continu.
public partial class ScanSection(string header, IEnumerable<CardListItem> matches) : ObservableObject
{
    public string Header { get; } = header;
    public ObservableCollection<CardListItem> Matches { get; } = new(matches);

    [ObservableProperty] private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
