using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using PCCleaner.Services.Cleaning;
using Wpf.Ui.Controls;

namespace PCCleaner.ViewModels;

public enum CleanupStatus
{
    NotScanned,
    Scanning,
    Scanned,
    Cleaning,
    Cleaned,
    RequiresAdmin,
    Failed,
}

public sealed partial class CleanupItemViewModel : ObservableObject
{
    public CleanupItemViewModel(ICleanupProvider provider, bool isChecked)
    {
        Provider = provider;
        IsAvailable = !provider.RequiresAdmin || AdminHelper.IsAdministrator;
        IsChecked = isChecked && IsAvailable;
        Status = IsAvailable ? CleanupStatus.NotScanned : CleanupStatus.RequiresAdmin;
    }

    public ICleanupProvider Provider { get; }

    public string Name => Provider.Name;

    public string Description => Provider.Description;

    public SymbolRegular Icon => Provider.Icon;

    public string? Warning => Provider.Warning;

    public bool IsAvailable { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DetailText), nameof(IsBusy), nameof(HasFoundJunk))]
    public partial CleanupStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial long FoundBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText), nameof(HasFoundJunk))]
    public partial int FoundFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial long FreedBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    public partial int SkippedFiles { get; set; }

    public bool IsBusy => Status is CleanupStatus.Scanning or CleanupStatus.Cleaning;

    public bool HasFoundJunk => Status == CleanupStatus.Scanned && FoundFiles > 0;

    public string StatusText => Status switch
    {
        CleanupStatus.RequiresAdmin => "Requires admin",
        CleanupStatus.Scanning => "Scanning…",
        CleanupStatus.Cleaning => FreedBytes > 0 ? ByteSize.Format(FreedBytes) : "Cleaning…",
        CleanupStatus.Scanned => FoundFiles == 0 ? "Clean" : ByteSize.Format(FoundBytes),
        CleanupStatus.Cleaned => "−" + ByteSize.Format(FreedBytes),
        CleanupStatus.Failed => "Error",
        _ => "—",
    };

    public string? DetailText => Status switch
    {
        CleanupStatus.Scanned when FoundFiles > 0 => $"{FoundFiles:N0} items",
        CleanupStatus.Cleaned when SkippedFiles > 0 => $"{SkippedFiles:N0} in use, skipped",
        CleanupStatus.Cleaned => "Cleaned",
        _ => null,
    };

    [RelayCommand]
    private void Toggle()
    {
        if (IsAvailable) IsChecked = !IsChecked;
    }
}
