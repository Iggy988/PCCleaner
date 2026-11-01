using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
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

        // Show the current page's (localized, live-updating) title as the header, and start each page at the top.
        RootNavigation.Navigated += (_, e) =>
        {
            if (e.Page is not Page page) return;
            PageHeader.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding(nameof(Page.Title)) { Source = page });
            Dispatcher.BeginInvoke(() => FindScrollViewer(page)?.ScrollToTop(), DispatcherPriority.Loaded);
        };

        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer viewer) return viewer;
        }

        return null;
    }
}
