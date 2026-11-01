using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;
using PCCleaner.Services.Localization;

namespace PCCleaner.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    public string Title => IsAdministrator ? Loc.T("App_Name") : Loc.F("App_TitleLimited", Loc.T("App_Name"));

    /// <summary>The limited-mode banner explains which features need admin rights; the user can dismiss it.</summary>
    [ObservableProperty]
    public partial bool ShowAdminBanner { get; set; } = !AdminHelper.IsAdministrator;

    [RelayCommand]
    private void DismissAdminBanner() => ShowAdminBanner = false;

    [RelayCommand]
    private static void RestartAsAdministrator()
    {
        if (AdminHelper.StartElevatedInstance())
            Application.Current.Shutdown();
    }
}
