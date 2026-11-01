using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace PCCleaner.ViewModels;

/// <summary>The user manual is static text; the only behavior is jumping to the page a section describes.</summary>
public sealed partial class HelpViewModel(INavigationService navigation)
{
    [RelayCommand]
    private void OpenPage(Type? pageType)
    {
        if (pageType is not null) navigation.Navigate(pageType);
    }
}
