using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using PCCleaner.Models;
using PCCleaner.Services;
using PCCleaner.Services.Localization;

namespace PCCleaner.Tests;

/// <summary>Tests that change the UI language share a collection so they never run in parallel with culture-sensitive tests.</summary>
[CollectionDefinition("Culture", DisableParallelization = true)]
public sealed class CultureCollection;

[Collection("Culture")]
public sealed partial class LocalizationTests
{
    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    public static TheoryData<string> Languages => new(LocalizationService.SupportedLanguages.Select(l => l.Code));

    [Fact]
    public void SerbianLatin_IsTheDefaultLanguage()
    {
        Assert.Equal("sr-Latn", LocalizationService.DefaultLanguage);
        Assert.Equal("sr-Latn", new AppSettings().Language);
        Assert.Equal("sr-Latn", LocalizationService.Normalize(null));
        Assert.Equal("sr-Latn", LocalizationService.Normalize("xx-YY"));
        Assert.Equal("de", LocalizationService.Normalize("DE"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguage_HasEveryString_WithMatchingPlaceholders(string code)
    {
        var neutral = Entries(CultureInfo.InvariantCulture);
        var translated = code == "en" ? neutral : Entries(CultureInfo.GetCultureInfo(code));

        Assert.True(neutral.Count > 250, $"Only {neutral.Count} neutral strings found.");
        Assert.Empty(neutral.Keys.Except(translated.Keys));
        foreach (var (key, english) in neutral)
        {
            Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{code}: {key} is empty");
            Assert.Equal(Placeholders(english), Placeholders(translated[key]));
        }
    }

    [Fact]
    public void Serbian_IsActuallyTranslated()
    {
        var english = Entries(CultureInfo.InvariantCulture);
        var serbian = Entries(CultureInfo.GetCultureInfo("sr-Latn"));

        Assert.Equal("Podešavanja", serbian["Nav_Settings"]);
        Assert.True(english.Count(e => e.Value != serbian[e.Key]) > english.Count * 0.8);
    }

    [Theory]
    [InlineData("sr", 1, "_One")]
    [InlineData("sr", 2, "_Few")]
    [InlineData("sr", 4, "_Few")]
    [InlineData("sr", 5, "_Many")]
    [InlineData("sr", 11, "_Many")]
    [InlineData("sr", 12, "_Many")]
    [InlineData("sr", 21, "_One")]
    [InlineData("ru", 22, "_Few")]
    [InlineData("ru", 111, "_Many")]
    [InlineData("en", 1, "_One")]
    [InlineData("en", 2, "_Many")]
    [InlineData("de", 0, "_Many")]
    [InlineData("zh", 1, "_Many")]
    public void PluralRules_FollowEachLanguage(string language, long count, string suffix) =>
        Assert.Equal(suffix, Loc.PluralSuffix(language, count));

    [Theory]
    [InlineData("sr-Latn", 2, "2 fajla")]
    [InlineData("sr-Latn", 5, "5 fajlova")]
    [InlineData("ru", 3, "3 файла")]
    [InlineData("en", 1, "1 file")]
    [InlineData("de", 1500, "1.500 Dateien")]
    public void Count_UsesLocalizedPluralsAndNumberFormat(string code, long count, string expected) =>
        WithLanguage(code, () => Assert.Equal(expected, Loc.Count("Count_Files", count)));

    [Theory]
    [InlineData("sr-Latn", "1,5 KB")]
    [InlineData("de", "1,5 KB")]
    [InlineData("ru", "1,5 КБ")]
    [InlineData("en", "1.5 KB")]
    [InlineData("zh-Hans", "1.5 KB")]
    public void FileSizes_UseLocalizedSeparatorsAndUnits(string code, string expected) =>
        WithLanguage(code, () => Assert.Equal(expected, ByteSize.Format(1536)));

    [Fact]
    public void SetLanguage_RaisesChangeNotifications()
    {
        WithLanguage("en", () =>
        {
            var raised = new List<string?>();
            LocalizationService.Instance.PropertyChanged += Handler;
            try
            {
                LocalizationService.Instance.SetLanguage("de");
                Assert.Contains("Item[]", raised);
                Assert.Equal("Einstellungen", LocalizationService.Instance["Nav_Settings"]);
            }
            finally
            {
                LocalizationService.Instance.PropertyChanged -= Handler;
            }

            void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e) => raised.Add(e.PropertyName);
        });
    }

    private static void WithLanguage(string code, Action test)
    {
        var previous = LocalizationService.Instance.LanguageCode;
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            LocalizationService.Instance.SetLanguage(code);
            test();
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = null;
            CultureInfo.DefaultThreadCurrentUICulture = null;
        }
    }

    private static Dictionary<string, string> Entries(CultureInfo culture)
    {
        var set = LocalizationService.Strings.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                  ?? throw new MissingSatelliteAssemblyException($"No strings for {culture.Name}");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static string[] Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Distinct().Order().ToArray();
}
