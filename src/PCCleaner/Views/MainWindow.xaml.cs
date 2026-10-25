using PCCleaner.Services;
using PCCleaner.ViewModels;
using PCCleaner.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace PCCleaner.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        AppThemeService themeService,
        SettingsService settings)
    {
        DataContext = viewModel;
        InitializeComponent();

        themeService.Attach(this);
        themeService.Apply(settings.Current.Theme);

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialogHost);
        navigationService.SetNavigationControl(RootNavigation);
        RootNavigation.SetPageProviderService(pageProvider);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
    }
}
