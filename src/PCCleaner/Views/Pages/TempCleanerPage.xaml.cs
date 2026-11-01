using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class TempCleanerPage : Page
{
    public TempCleanerPage(TempCleanerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
