using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PCCleaner.Services;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Tweaks;
using PCCleaner.ViewModels;
using PCCleaner.Views;
using PCCleaner.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace PCCleaner;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Console tools (chkdsk, DISM...) print in the OEM code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _services = ConfigureServices();
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // WPF-UI infrastructure
        services.AddNavigationViewPageProvider();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // App services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AppThemeService>();
        services.AddSingleton<CleanupCatalog>();
        services.AddSingleton<TweakCatalog>();
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<SystemActions>();
        services.AddSingleton<RestorePointService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<HealthToolsService>();

        // View models and views (singletons keep page state while navigating)
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<CleanerViewModel>();
        services.AddSingleton<CleanerPage>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<StartupPage>();
        services.AddSingleton<OptimizeViewModel>();
        services.AddSingleton<OptimizePage>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<HealthPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsPage>();

        return services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"Unhandled exception: {e.Exception}");
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log in {SettingsService.LogsDirectory}.",
            "PC Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
