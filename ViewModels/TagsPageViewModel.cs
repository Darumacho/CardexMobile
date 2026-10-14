using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cardex.Models;
using CardexMobile.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace CardexMobile.ViewModels;

public partial class TagItem : ObservableObject
{
    public static readonly TagItem NoTag = new() { Id = 0, Name = "No tag", Color = "#444455" };

    public int Id { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchColor))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchColor))]
    private string _color = "#8888aa";

    public Microsoft.Maui.Graphics.Color SwatchColor => Microsoft.Maui.Graphics.Color.FromArgb(Color);

    [RelayCommand] private void SetColor(string color) => Color = color;
}

// Équivalent de TagSectionViewModel desktop — groupe par tag les cartes qui l'ont, repliable comme
// les sections de CollectionPage. État replié/déplié persisté par tag (ExpandedStateStore), clé
// basée sur Tag.Id (stable, contrairement au nom qui peut être renommé).
public partial class TagSectionItem : ObservableObject
{
    public TagItem Tag { get; }
    public ObservableCollection<CardListItem> Cards { get; } = [];

    [ObservableProperty] private bool _isExpanded;
    public string Chevron => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Chevron));
        ExpandedStateStore.Set($"tag.{Tag.Id}", value);
    }

    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public TagSectionItem(TagItem tag)
    {
        Tag = tag;
        _isExpanded = ExpandedStateStore.Get($"tag.{Tag.Id}");
    }
}

// Équivalent mobile de l'onglet "My Tags" de SettingsWindow desktop — CRUD uniquement. L'affichage
// des cartes groupées par tag (TagSections desktop) vit sur CollectionPage, pas ici, pour matcher
// la page d'accueil desktop (TagSections apparaît sous Duplicates sur MainWindow, pas dans Settings).
public partial class TagsPageViewModel : ObservableObject
{
    private readonly MobileAppDbContext _db;
    private readonly DbInitializer _dbInit;

    public ObservableCollection<TagItem> Tags { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _newTagName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingSwatchColor))]
    private string _pendingTagColor = "#3a7fc1";

    public Microsoft.Maui.Graphics.Color PendingSwatchColor => Microsoft.Maui.Graphics.Color.FromArgb(PendingTagColor);

    public bool HasTags    => Tags.Count > 0;
    public bool HasNoTags   => !HasTags;

    public TagsPageViewModel(MobileAppDbContext db, DbInitializer dbInit)
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
            var tags = await _db.Tags.OrderBy(t => t.Id).ToListAsync();
            Tags.Clear();
            foreach (var t in tags)
                Tags.Add(new TagItem { Id = t.Id, Name = t.Name, Color = t.Color });
            NotifyHasTagsChanged();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void SelectPendingColor(string color) => PendingTagColor = color;

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagName)) return;
        var tag = new Tag { Name = NewTagName.Trim(), Color = PendingTagColor };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        Tags.Add(new TagItem { Id = tag.Id, Name = tag.Name, Color = tag.Color });
        NewTagName = "";
        NotifyHasTagsChanged();
        await Cardex.Services.AchievementService.CheckAsync("first_tag", _db);
    }

    [RelayCommand]
    private async Task UpdateTagAsync(TagItem item)
    {
        var tag = await _db.Tags.FindAsync(item.Id);
        if (tag is null) return;
        tag.Name = item.Name;
        tag.Color = item.Color;
        await _db.SaveChangesAsync();
    }

    [RelayCommand]
    private async Task DeleteTagAsync(TagItem item)
    {
        var cardTags = await _db.CardTags.Where(ct => ct.TagId == item.Id).ToListAsync();
        _db.CardTags.RemoveRange(cardTags);
        var tag = await _db.Tags.FindAsync(item.Id);
        if (tag is not null) _db.Tags.Remove(tag);
        await _db.SaveChangesAsync();

        Tags.Remove(item);
        NotifyHasTagsChanged();
    }

    private void NotifyHasTagsChanged()
    {
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasNoTags));
    }
}
