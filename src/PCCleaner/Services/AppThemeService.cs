using System.Windows;
using PCCleaner.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PCCleaner.Services;

public sealed class AppThemeService
{
    private Window? _window;
    private bool _watchingSystem;

    public void Attach(Window window) => _window = window;

    public void Apply(ThemePreference preference)
    {
        if (preference == ThemePreference.System)
        {
            ApplicationThemeManager.ApplySystemTheme(true);
            if (_window is not null && !_watchingSystem)
            {
                SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, true);
                _watchingSystem = true;
            }

            return;
        }

        if (_window is not null && _watchingSystem)
        {
            SystemThemeWatcher.UnWatch(_window);
            _watchingSystem = false;
        }

        ApplicationThemeManager.Apply(
            preference == ThemePreference.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);
    }
}
