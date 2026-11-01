using System.Windows;
using System.Windows.Media;
using PCCleaner.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PCCleaner.Services;

public sealed class AppThemeService
{
    /// <summary>Ultimate PC Cleaner blue (from the logo); used instead of the Windows accent color.</summary>
    public static readonly Color BrandAccent = Color.FromRgb(0x1E, 0x88, 0xFF);

    private Window? _window;
    private bool _watchingSystem;
    private bool _applyingAccent;

    public AppThemeService()
    {
        // Windows switching light/dark (while following the system theme) resets accents; re-apply the brand color.
        ApplicationThemeManager.Changed += (theme, _) => ApplyAccent(theme);
    }

    public void Attach(Window window) => _window = window;

    public void Apply(ThemePreference preference)
    {
        if (preference == ThemePreference.System)
        {
            ApplicationThemeManager.ApplySystemTheme(false);
            if (_window is not null && !_watchingSystem)
            {
                SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, false);
                _watchingSystem = true;
            }
        }
        else
        {
            if (_window is not null && _watchingSystem)
            {
                SystemThemeWatcher.UnWatch(_window);
                _watchingSystem = false;
            }

            ApplicationThemeManager.Apply(
                preference == ThemePreference.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.Mica,
                false);
        }

        ApplyAccent(ApplicationThemeManager.GetAppTheme());
    }

    private void ApplyAccent(ApplicationTheme theme)
    {
        if (_applyingAccent) return;
        _applyingAccent = true;
        try
        {
            ApplicationAccentColorManager.Apply(BrandAccent, theme, false, false);
        }
        finally
        {
            _applyingAccent = false;
        }
    }
}
