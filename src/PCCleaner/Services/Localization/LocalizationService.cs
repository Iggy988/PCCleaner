using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace PCCleaner.Services.Localization;

public sealed record LanguageOption(string Code, string NativeName)
{
    // Screen readers announce ComboBox items by ToString().
    public override string ToString() => NativeName;
}

/// <summary>
/// Central access to the UI strings in Resources/Strings/Strings*.resx. XAML binds to the indexer
/// (see <see cref="TrExtension"/>), so switching the language updates every bound string immediately;
/// view models listen to <see cref="LanguageChanged"/> to refresh computed text.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string DefaultLanguage = "sr-Latn";

    internal static readonly ResourceManager Strings = new("PCCleaner.Resources.Strings.Strings", typeof(LocalizationService).Assembly);

    private LocalizationService()
    {
        Culture = CultureInfo.GetCultureInfo(DefaultLanguage);
    }

    public static LocalizationService Instance { get; } = new();

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("sr-Latn", "Srpski (latinica)"),
        new("en", "English"),
        new("ru", "Русский"),
        new("de", "Deutsch"),
        new("zh-Hans", "简体中文"),
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public CultureInfo Culture { get; private set; }

    public string LanguageCode => SupportedLanguages.First(l => l.Code == Culture.Name).Code;

    /// <summary>The string for <paramref name="key"/> in the current language (English if a translation is missing).</summary>
    public string this[string key] => Strings.GetString(key, Culture) ?? key;

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    /// <summary>Returns a supported language code for <paramref name="code"/>, or the default (Serbian) if it isn't supported.</summary>
    public static string Normalize(string? code) =>
        SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? DefaultLanguage;

    public void SetLanguage(string? code)
    {
        var culture = CultureInfo.GetCultureInfo(Normalize(code));
        var changed = !Equals(culture, Culture);
        Culture = culture;

        // Number, date and size formatting follows the UI language, including on worker threads.
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (!changed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Short-hand for code: <c>Loc.T("Key")</c> and <c>Loc.F("Key", args)</c>.</summary>
public static class Loc
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string F(string key, params object?[] args) => LocalizationService.Instance.Format(key, args);

    /// <summary>
    /// "1 file" / "3 files" / "5 files": picks <c>{key}_One</c>, <c>{key}_Few</c> or <c>{key}_Many</c> by the current
    /// language's plural rules and formats the count.
    /// </summary>
    public static string Count(string key, long count) =>
        F(key + PluralSuffix(LocalizationService.Instance.Culture.TwoLetterISOLanguageName, count),
            count.ToString("N0", LocalizationService.Instance.Culture));

    /// <summary>CLDR cardinal rules for the supported languages (Serbian/Russian have one/few/many; Chinese has one form).</summary>
    internal static string PluralSuffix(string language, long count)
    {
        var n = Math.Abs(count);
        switch (language)
        {
            case "sr" or "ru":
                var mod10 = n % 10;
                var mod100 = n % 100;
                if (mod10 == 1 && mod100 != 11) return "_One";
                if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "_Few";
                return "_Many";
            case "zh":
                return "_Many";
            default:
                return n == 1 ? "_One" : "_Many";
        }
    }
}
