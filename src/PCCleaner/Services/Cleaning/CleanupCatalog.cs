using System.IO;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning.Temp;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

/// <summary>All cleanup categories the app offers, in display order. Names/descriptions live in the string resources.</summary>
public sealed class CleanupCatalog
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private readonly SettingsService _settings;
    private readonly TempLocationResolver _tempLocations;

    public CleanupCatalog(SettingsService settings, TempLocationResolver tempLocations)
    {
        _settings = settings;
        _tempLocations = tempLocations;
        Providers = BuildProviders().ToList();
    }

    public IReadOnlyList<ICleanupProvider> Providers { get; }

    /// <summary>Categories that cover the same folders as the Temp Files page (used to avoid counting them twice).</summary>
    public static IReadOnlySet<string> TempLocationIds { get; } = new HashSet<string> { "user-temp", "windows-temp", "prefetch" };

    private TimeSpan TempMinAge => _settings.Current.SkipRecentTempFiles ? TimeSpan.FromHours(24) : TimeSpan.Zero;

    /// <summary>Same resolution and rules as the Temp Files page; an unresolvable location yields no targets.</summary>
    private IEnumerable<FolderTarget> TempTargets(TempLocationKind kind)
    {
        var location = _tempLocations.Resolve(kind, TempMinAge);
        return location.IsResolved ? [location.ToFolderTarget()] : [];
    }

    private IEnumerable<ICleanupProvider> BuildProviders()
    {
        yield return new FolderCleanupProvider("user-temp", CleanupGroup.System, SymbolRegular.DocumentBulletList24,
            () => TempTargets(TempLocationKind.UserTemp));

        yield return new FolderCleanupProvider("windows-temp", CleanupGroup.System, SymbolRegular.Window24,
            () => TempTargets(TempLocationKind.WindowsTemp))
        { RequiresAdmin = true };

        yield return new FolderCleanupProvider("windows-update", CleanupGroup.System, SymbolRegular.ArrowDownload24,
            () => [new FolderTarget { Path = Path.Combine(Windows, "SoftwareDistribution", "Download") }])
        {
            RequiresAdmin = true,
            ServicesToStop = ["wuauserv", "bits"],
        };

        yield return new FolderCleanupProvider("delivery-optimization", CleanupGroup.System, SymbolRegular.CloudArrowDown24,
            () => [new FolderTarget { Path = Path.Combine(Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }])
        {
            RequiresAdmin = true,
            ServicesToStop = ["dosvc"],
        };

        yield return new FolderCleanupProvider("error-reports", CleanupGroup.System, SymbolRegular.DocumentError24,
            () =>
            [
                new FolderTarget { Path = Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive") },
                new FolderTarget { Path = Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue") },
                new FolderTarget { Path = Path.Combine(ProgramData, @"Microsoft\Windows\WER\Temp") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportQueue") },
            ])
        { RequiresAdmin = true };

        yield return new FolderCleanupProvider("memory-dumps", CleanupGroup.System, SymbolRegular.Bug24,
            () =>
            [
                new FolderTarget { Path = Path.Combine(Windows, "Minidump"), SearchPattern = "*.dmp" },
                new FolderTarget { Path = Windows, SearchPattern = "MEMORY.DMP", Recursive = false },
                new FolderTarget { Path = Path.Combine(LocalAppData, "CrashDumps"), SearchPattern = "*.dmp" },
            ])
        { RequiresAdmin = true };

        yield return new FolderCleanupProvider("windows-logs", CleanupGroup.System, SymbolRegular.History24,
            () => [new FolderTarget { Path = Path.Combine(Windows, "Logs"), SearchPattern = "*.log", MinAge = TimeSpan.FromDays(7) }])
        { RequiresAdmin = true };

        yield return new RecycleBinProvider();

        yield return new FolderCleanupProvider("prefetch", CleanupGroup.System, SymbolRegular.Flash24,
            () => TempTargets(TempLocationKind.Prefetch))
        {
            RequiresAdmin = true,
            IsRecommended = false,
            HasWarning = true,
        };

        yield return new FolderCleanupProvider("thumbnail-cache", CleanupGroup.System, SymbolRegular.Image24,
            () => [new FolderTarget { Path = Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer"), SearchPattern = "thumbcache_*.db", Recursive = false }])
        {
            IsRecommended = false,
            HasWarning = true,
        };

        foreach (var browser in ChromiumBrowsers())
            yield return browser;

        var firefoxProfiles = Path.Combine(LocalAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            yield return new FolderCleanupProvider("firefox-cache", CleanupGroup.Browsers, SymbolRegular.Globe24,
                () => SafeSubDirectories(firefoxProfiles).Select(profile => new FolderTarget { Path = Path.Combine(profile, "cache2") }),
                descriptionKey: "Cleanup_BrowserCache_Desc")
            {
                BlockingProcesses = ["firefox"],
                BlockingAppName = "Mozilla Firefox",
            };
        }

        yield return new FolderCleanupProvider("shader-cache", CleanupGroup.Applications, SymbolRegular.Games24,
            () =>
            [
                new FolderTarget { Path = Path.Combine(LocalAppData, "D3DSCache") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"NVIDIA\DXCache") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"NVIDIA\GLCache") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"AMD\DxCache") },
            ])
        {
            IsRecommended = false,
            HasWarning = true,
        };
    }

    private static IEnumerable<ICleanupProvider> ChromiumBrowsers()
    {
        (string Id, string AppName, string UserData, string Process)[] browsers =
        [
            ("edge-cache", "Microsoft Edge", Path.Combine(LocalAppData, @"Microsoft\Edge\User Data"), "msedge"),
            ("chrome-cache", "Google Chrome", Path.Combine(LocalAppData, @"Google\Chrome\User Data"), "chrome"),
            ("brave-cache", "Brave", Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data"), "brave"),
        ];

        foreach (var (id, appName, userData, process) in browsers)
        {
            if (!Directory.Exists(userData)) continue;

            yield return new FolderCleanupProvider(id, CleanupGroup.Browsers, SymbolRegular.Globe24,
                () => ChromiumTargets(userData), descriptionKey: "Cleanup_BrowserCache_Desc")
            {
                BlockingProcesses = [process],
                BlockingAppName = appName,
            };
        }
    }

    private static IEnumerable<FolderTarget> ChromiumTargets(string userData)
    {
        yield return new FolderTarget { Path = Path.Combine(userData, "ShaderCache") };
        yield return new FolderTarget { Path = Path.Combine(userData, "GrShaderCache") };

        // Every profile folder ("Default", "Profile 1", ...) has a Preferences file.
        foreach (var profile in SafeSubDirectories(userData).Where(d => File.Exists(Path.Combine(d, "Preferences"))))
        {
            yield return new FolderTarget { Path = Path.Combine(profile, "Cache") };
            yield return new FolderTarget { Path = Path.Combine(profile, "Code Cache") };
            yield return new FolderTarget { Path = Path.Combine(profile, "GPUCache") };
        }
    }

    private static IEnumerable<string> SafeSubDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
