using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class SecurityPage : Page
{
    public SecurityPage(SecurityViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Activate();
    }

    private void OnOutputChanged(object sender, TextChangedEventArgs e) => OutputBox.ScrollToEnd();
}
