using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCCleaner.Controls;
using PCCleaner.Models;
using PCCleaner.Services;
using PCCleaner.Services.Localization;
using PCCleaner.Services.Security;

namespace PCCleaner.ViewModels;

public sealed class ThemeOption(ThemePreference value, string labelKey) : ObservableObject
{
    public ThemePreference Value { get; } = value;

    public string Label => Loc.T(labelKey);

    public void RefreshText() => OnPropertyChanged(nameof(Label));

    public override string ToString() => Label;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AppThemeService _theme;
    private readonly VirusTotalClient _virusTotal;

    public SettingsViewModel(SettingsService settings, AppThemeService theme, VirusTotalClient virusTotal)
    {
        _settings = settings;
        _theme = theme;
        _virusTotal = virusTotal;
        var current = settings.Current;
        SelectedLanguage = Languages.First(l => l.Code == LocalizationService.Instance.LanguageCode);
        SelectedTheme = ThemeOptions.First(o => o.Value == current.Theme);
        ReduceMotion = current.ReduceMotion;
        SkipRecentTempFiles = current.SkipRecentTempFiles;
        CreateRestorePoint = current.CreateRestorePoint;
        RememberSelections = current.RememberSelections;
        VirusTotalEnabled = current.VirusTotalEnabled;
        HasVirusTotalKey = !string.IsNullOrEmpty(virusTotal.GetApiKey());

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var option in ThemeOptions) option.RefreshText();
        };
    }

    public IReadOnlyList<LanguageOption> Languages => LocalizationService.SupportedLanguages;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "Settings_ThemeSystem"),
        new(ThemePreference.Light, "Settings_ThemeLight"),
        new(ThemePreference.Dark, "Settings_ThemeDark"),
    ];

    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

    public string DataFolder => SettingsService.DataDirectory;

    public bool IsAdministrator => AdminHelper.IsAdministrator;

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool ReduceMotion { get; set; }

    [ObservableProperty]
    public partial bool SkipRecentTempFiles { get; set; }

    [ObservableProperty]
    public partial bool CreateRestorePoint { get; set; }

    [ObservableProperty]
    public partial bool RememberSelections { get; set; }

    [ObservableProperty]
    public partial bool VirusTotalEnabled { get; set; }

    [ObservableProperty]
    public partial bool HasVirusTotalKey { get; set; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null || LocalizationService.Instance.LanguageCode == value.Code) return;
        _settings.Current.Language = value.Code;
        _settings.Save();
        LocalizationService.Instance.SetLanguage(value.Code);
        AppLog.Write($"Language changed to {value.Code}");
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (value is null || _settings.Current.Theme == value.Value) return;
        _settings.Current.Theme = value.Value;
        _settings.Save();
        _theme.Apply(value.Value);
    }

    partial void OnReduceMotionChanged(bool value)
    {
        MotionSettings.Instance.UserPrefersReducedMotion = value;
        Save(s => s.ReduceMotion = value);
    }

    partial void OnSkipRecentTempFilesChanged(bool value) => Save(s => s.SkipRecentTempFiles = value);

    partial void OnCreateRestorePointChanged(bool value) => Save(s => s.CreateRestorePoint = value);

    partial void OnRememberSelectionsChanged(bool value) => Save(s => s.RememberSelections = value);

    partial void OnVirusTotalEnabledChanged(bool value) => Save(s => s.VirusTotalEnabled = value);

    /// <summary>The key comes from a PasswordBox (not bindable), so the view passes it in.</summary>
    [RelayCommand]
    private void SaveVirusTotalKey(string? apiKey)
    {
        _virusTotal.SetApiKey(apiKey);
        HasVirusTotalKey = !string.IsNullOrWhiteSpace(apiKey);
    }

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
