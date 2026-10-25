using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class OptimizePage : Page
{
    public OptimizePage(OptimizeViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Activate();
    }
}
