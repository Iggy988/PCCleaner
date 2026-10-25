using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class CleanerPage : Page
{
    public CleanerPage(CleanerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
