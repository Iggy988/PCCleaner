using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Services;

namespace PCCleaner.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public bool IsAdministrator => AdminHelper.IsAdministrator;

    public string Title => IsAdministrator ? "PC Cleaner" : "PC Cleaner (limited mode)";

    [RelayCommand]
    private static void RestartAsAdministrator()
    {
        if (AdminHelper.StartElevatedInstance())
            Application.Current.Shutdown();
    }
}
