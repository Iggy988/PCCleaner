using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class SystemPage : Page
{
    public SystemPage(SystemViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Activate();
    }
}
