using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Models;
using PCCleaner.Services;

namespace PCCleaner.ViewModels;

public sealed record ThemeOption(ThemePreference Value, string Label);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AppThemeService _theme;

    public SettingsViewModel(SettingsService settings, AppThemeService theme)
    {
        _settings = settings;
        _theme = theme;
        var current = settings.Current;
        SelectedTheme = ThemeOptions.First(o => o.Value == current.Theme);
        SkipRecentTempFiles = current.SkipRecentTempFiles;
        CreateRestorePoint = current.CreateRestorePoint;
        RememberSelections = current.RememberSelections;
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "Use Windows setting"),
        new(ThemePreference.Light, "Light"),
        new(ThemePreference.Dark, "Dark"),
    ];

    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string DataFolder => SettingsService.DataDirectory;

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool SkipRecentTempFiles { get; set; }

    [ObservableProperty]
    public partial bool CreateRestorePoint { get; set; }

    [ObservableProperty]
    public partial bool RememberSelections { get; set; }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (_settings.Current.Theme == value.Value) return;
        _settings.Current.Theme = value.Value;
        _settings.Save();
        _theme.Apply(value.Value);
    }

    partial void OnSkipRecentTempFilesChanged(bool value) => Save(s => s.SkipRecentTempFiles = value);

    partial void OnCreateRestorePointChanged(bool value) => Save(s => s.CreateRestorePoint = value);

    partial void OnRememberSelectionsChanged(bool value) => Save(s => s.RememberSelections = value);

    [RelayCommand]
    private static void OpenDataFolder() => OpenFolder(SettingsService.DataDirectory);

    [RelayCommand]
    private static void OpenLogs() => OpenFolder(SettingsService.LogsDirectory);

    private void Save(Action<AppSettings> change)
    {
        change(_settings.Current);
        _settings.Save();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
