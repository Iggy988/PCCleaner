using System.Windows.Controls;
using PCCleaner.ViewModels;

namespace PCCleaner.Views.Pages;

public partial class HelpPage : Page
{
    public HelpPage(HelpViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
