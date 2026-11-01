using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PCCleaner.Controls;
using PCCleaner.Services;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Cleaning.Temp;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Security;
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

        // Language and motion preferences apply before any window is created.
        // "--language=ru" overrides the saved language for this session only (testing/support).
        var settings = _services.GetRequiredService<SettingsService>();
        var languageArg = e.Args.FirstOrDefault(a => a.StartsWith("--language=", StringComparison.OrdinalIgnoreCase));
        LocalizationService.Instance.SetLanguage(languageArg?["--language=".Length..] ?? settings.Current.Language);
        MotionSettings.Instance.UserPrefersReducedMotion = settings.Current.ReduceMotion;

        var window = _services.GetRequiredService<MainWindow>();
        ApplyXamlLanguage(window);
        LocalizationService.Instance.LanguageChanged += (_, _) => ApplyXamlLanguage(window);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Makes XAML-side formatting (StringFormat, dates) use the selected UI language.</summary>
    private static void ApplyXamlLanguage(FrameworkElement root) =>
        root.Language = XmlLanguage.GetLanguage(LocalizationService.Instance.Culture.IetfLanguageTag);

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
        services.AddSingleton<TempLocationResolver>();
        services.AddSingleton<TempFileCleaner>();
        services.AddSingleton<CleanupCatalog>();
        services.AddSingleton<TweakCatalog>();
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<SystemActions>();
        services.AddSingleton<RestorePointService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<HealthToolsService>();
        services.AddSingleton<ComputerNameService>();
        services.AddSingleton<WindowsUpdateService>();
        services.AddSingleton<DefenderService>();
        services.AddSingleton<AmsiScanner>();
        services.AddSingleton<SuspiciousItemScanner>();
        services.AddSingleton<VirusTotalClient>();
        services.AddSingleton<QuarantineService>();

        // View models and views (singletons keep page state while navigating)
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<CleanerViewModel>();
        services.AddSingleton<CleanerPage>();
        services.AddSingleton<TempCleanerViewModel>();
        services.AddSingleton<TempCleanerPage>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<StartupPage>();
        services.AddSingleton<SecurityViewModel>();
        services.AddSingleton<SecurityPage>();
        services.AddSingleton<OptimizeViewModel>();
        services.AddSingleton<OptimizePage>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<HealthPage>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<SystemPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<HelpViewModel>();
        services.AddSingleton<HelpPage>();

        return services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"Unhandled exception: {e.Exception}");
        MessageBox.Show(Loc.F("App_UnhandledError", e.Exception.Message, SettingsService.LogsDirectory),
            Loc.T("App_Name"), MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
