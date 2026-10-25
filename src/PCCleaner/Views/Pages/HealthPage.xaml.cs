using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class HealthPage : Page
{
    public HealthPage(HealthViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnOutputChanged(object sender, TextChangedEventArgs e) => OutputBox.ScrollToEnd();
}
