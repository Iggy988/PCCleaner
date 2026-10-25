using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Models;
using PCCleaner.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCCleaner.ViewModels;

public sealed partial class StartupItemViewModel(StartupItem item, StartupViewModel owner) : ObservableObject
{
    public StartupItem Item { get; } = item;

    public string Name => Item.Name;

    public string Publisher => Item.Publisher ?? "Unknown publisher";

    public string Command => Item.Command;

    public string Location => Item.Location;

    public ImageSource? Icon => Item.Icon;

    public bool HasIcon => Item.Icon is not null;

    public bool CanToggle => !Item.IsMachineWide || AdminHelper.IsAdministrator;

    public bool CanOpenLocation => File.Exists(Item.FilePath);

    public bool IsEnabled
    {
        get => Item.IsEnabled;
        set
        {
            if (value != Item.IsEnabled) owner.SetEnabled(this, value);
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsEnabled));

    [RelayCommand]
    private void OpenLocation() => StartupService.OpenFileLocation(Item);
}

public sealed partial class StartupViewModel : ObservableObject
{
    private readonly StartupService _service;
    private readonly ISnackbarService _snackbar;
    private bool _loaded;

    public StartupViewModel(StartupService service, ISnackbarService snackbar)
    {
        _service = service;
        _snackbar = snackbar;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Matches;
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    public ICollectionView View { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public string Summary => $"{Items.Count(i => i.IsEnabled)} enabled · {Items.Count(i => !i.IsEnabled)} disabled";

    public bool ShowAdminNotice => !AdminHelper.IsAdministrator && Items.Any(i => i.Item.IsMachineWide);

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public void Activate()
    {
        if (!_loaded) _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var items = await _service.GetItemsAsync();
            Items.Clear();
            foreach (var item in items) Items.Add(new StartupItemViewModel(item, this));
            _loaded = true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Loading startup items failed: {ex}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ShowAdminNotice));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    internal void SetEnabled(StartupItemViewModel vm, bool enabled)
    {
        try
        {
            _service.SetEnabled(vm.Item, enabled);
            AppLog.Write($"Startup item {(enabled ? "enabled" : "disabled")}: {vm.Name}");
            _snackbar.Show(enabled ? "Startup app enabled" : "Startup app disabled",
                $"{vm.Name} will {(enabled ? "" : "no longer ")}start with Windows.",
                ControlAppearance.Success, new SymbolIcon(SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            _snackbar.Show("Couldn't change startup app", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
        }

        vm.Refresh();
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnSearchTextChanged(string value) => View.Refresh();

    private bool Matches(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var item = (StartupItemViewModel)obj;
        return item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
               || item.Publisher.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
               || item.Command.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
    }
}
