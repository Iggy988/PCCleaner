using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCleaner.Services;
using Wpf.Ui.Controls;

namespace PCCleaner.ViewModels;

public sealed class CleanupGroupViewModel : ObservableObject
{
    public CleanupGroupViewModel(string name, SymbolRegular icon, IReadOnlyList<CleanupItemViewModel> items)
    {
        Name = name;
        Icon = icon;
        Items = items;
        foreach (var item in items) item.PropertyChanged += OnItemChanged;
    }

    public string Name { get; }

    public SymbolRegular Icon { get; }

    public IReadOnlyList<CleanupItemViewModel> Items { get; }

    /// <summary>Three-state "select all" for the group (null = some selected).</summary>
    public bool? IsAllChecked
    {
        get
        {
            var available = Items.Where(i => i.IsAvailable).ToList();
            if (available.Count == 0 || available.All(i => !i.IsChecked)) return false;
            return available.All(i => i.IsChecked) ? true : null;
        }
        set
        {
            var check = value ?? false;
            foreach (var item in Items.Where(i => i.IsAvailable)) item.IsChecked = check;
        }
    }

    public string Summary
    {
        get
        {
            var selected = Items.Count(i => i.IsChecked);
            var bytes = Items.Where(i => i.IsChecked && i.Status == CleanupStatus.Scanned).Sum(i => i.FoundBytes);
            var text = $"{selected} of {Items.Count} selected";
            return bytes > 0 ? $"{text} · {ByteSize.Format(bytes)}" : text;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupItemViewModel.IsChecked))
            OnPropertyChanged(nameof(IsAllChecked));

        if (e.PropertyName is nameof(CleanupItemViewModel.IsChecked) or nameof(CleanupItemViewModel.Status) or nameof(CleanupItemViewModel.FoundBytes))
            OnPropertyChanged(nameof(Summary));
    }
}
