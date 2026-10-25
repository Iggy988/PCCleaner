using System.IO;
using PCCleaner.Models;
using Wpf.Ui.Controls;

namespace PCCleaner.Services.Cleaning;

/// <summary>All cleanup categories the app offers, in display order.</summary>
public sealed class CleanupCatalog
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private readonly SettingsService _settings;

    public CleanupCatalog(SettingsService settings)
    {
        _settings = settings;
        Providers = BuildProviders().ToList();
    }

    public IReadOnlyList<ICleanupProvider> Providers { get; }

    private TimeSpan TempMinAge => _settings.Current.SkipRecentTempFiles ? TimeSpan.FromHours(24) : TimeSpan.Zero;

    private IEnumerable<ICleanupProvider> BuildProviders()
    {
        var userTemp = Path.GetTempPath();

        yield return new FolderCleanupProvider("user-temp", "User temporary files",
            "Leftovers that apps and installers write to your personal Temp folder.",
            CleanupGroup.System, SymbolRegular.DocumentBulletList24,
            () => [new FolderTarget
            {
                Path = userTemp,
                MinAge = TempMinAge,
                // Self-extracting .NET apps cache their native files here.
                ExcludedPaths = [Path.Combine(userTemp, ".net")],
            }]);

        yield return new FolderCleanupProvider("windows-temp", "Windows temporary files",
            "Temporary files created by Windows and system services.",
            CleanupGroup.System, SymbolRegular.Window24,
            () => [new FolderTarget { Path = Path.Combine(Windows, "Temp"), MinAge = TempMinAge }])
        { RequiresAdmin = true };

        yield return new FolderCleanupProvider("windows-update", "Windows Update cache",
            "Update packages that were already downloaded and installed.",
            CleanupGroup.System, SymbolRegular.ArrowDownload24,
            () => [new FolderTarget { Path = Path.Combine(Windows, "SoftwareDistribution", "Download") }])
        {
            RequiresAdmin = true,
            ServicesToStop = ["wuauserv", "bits"],
        };

        yield return new FolderCleanupProvider("delivery-optimization", "Delivery Optimization cache",
            "Update files Windows keeps to share with other PCs.",
            CleanupGroup.System, SymbolRegular.CloudArrowDown24,
            () => [new FolderTarget { Path = Path.Combine(Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }])
        {
            RequiresAdmin = true,
            ServicesToStop = ["dosvc"],
        };

        yield return new FolderCleanupProvider("error-reports", "Windows error reports",
            "Crash and problem reports that were already sent or queued.",
            CleanupGroup.System, SymbolRegular.DocumentError24,
            () =>
            [
                new FolderTarget { Path = Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive") },
                new FolderTarget { Path = Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue") },
                new FolderTarget { Path = Path.Combine(ProgramData, @"Microsoft\Windows\WER\Temp") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportQueue") },
            ])
        { RequiresAdmin = true };

        yield return new FolderCleanupProvider("memory-dumps", "Memory dumps",
            "Diagnostic dump files written when Windows or an app crashed.",
            CleanupGroup.System, SymbolRegular.Bug24,
            () =>
            [
                new FolderTarget { Path = Path.Combine(Windows, "Minidump"), SearchPattern = "*.dmp" },
                new FolderTarget { Path = Windows, SearchPattern = "MEMORY.DMP", Recursive = false },
                new FolderTarget { Path = Path.Combine(LocalAppData, "CrashDumps"), SearchPattern = "*.dmp" },
            ])
        { RequiresAdmin = true };

        yield return new FolderCleanupProvider("windows-logs", "Old Windows log files",
            "Setup and servicing logs older than 7 days.",
            CleanupGroup.System, SymbolRegular.History24,
            () => [new FolderTarget { Path = Path.Combine(Windows, "Logs"), SearchPattern = "*.log", MinAge = TimeSpan.FromDays(7) }])
        { RequiresAdmin = true };

        yield return new RecycleBinProvider();

        yield return new FolderCleanupProvider("prefetch", "Prefetch data",
            "Launch traces Windows uses to start your apps faster.",
            CleanupGroup.System, SymbolRegular.Flash24,
            () => [new FolderTarget { Path = Path.Combine(Windows, "Prefetch"), SearchPattern = "*.pf", Recursive = false }])
        {
            RequiresAdmin = true,
            IsRecommended = false,
            Warning = "Windows rebuilds this automatically. Apps may start a bit slower the first time after cleaning.",
        };

        yield return new FolderCleanupProvider("thumbnail-cache", "Thumbnail cache",
            "Cached picture and video previews shown in File Explorer.",
            CleanupGroup.System, SymbolRegular.Image24,
            () => [new FolderTarget { Path = Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer"), SearchPattern = "thumbcache_*.db", Recursive = false }])
        {
            IsRecommended = false,
            Warning = "File Explorer keeps some of these open; they are rebuilt as you browse folders.",
        };

        foreach (var browser in ChromiumBrowsers())
            yield return browser;

        var firefoxProfiles = Path.Combine(LocalAppData, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            yield return new FolderCleanupProvider("firefox-cache", "Mozilla Firefox cache",
                "Cached web pages and images. Cookies, history and passwords are kept.",
                CleanupGroup.Browsers, SymbolRegular.Globe24,
                () => SafeSubDirectories(firefoxProfiles).Select(profile => new FolderTarget { Path = Path.Combine(profile, "cache2") }))
            { BlockingProcesses = ["firefox"] };
        }

        yield return new FolderCleanupProvider("shader-cache", "DirectX shader cache",
            "Compiled graphics shaders cached by games and apps.",
            CleanupGroup.Applications, SymbolRegular.Games24,
            () =>
            [
                new FolderTarget { Path = Path.Combine(LocalAppData, "D3DSCache") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"NVIDIA\DXCache") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"NVIDIA\GLCache") },
                new FolderTarget { Path = Path.Combine(LocalAppData, @"AMD\DxCache") },
            ])
        {
            IsRecommended = false,
            Warning = "Games rebuild shaders on next launch, which can cause brief stutter the first time.",
        };
    }

    private static IEnumerable<ICleanupProvider> ChromiumBrowsers()
    {
        (string Id, string Name, string UserData, string Process)[] browsers =
        [
            ("edge-cache", "Microsoft Edge cache", Path.Combine(LocalAppData, @"Microsoft\Edge\User Data"), "msedge"),
            ("chrome-cache", "Google Chrome cache", Path.Combine(LocalAppData, @"Google\Chrome\User Data"), "chrome"),
            ("brave-cache", "Brave cache", Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data"), "brave"),
        ];

        foreach (var (id, name, userData, process) in browsers)
        {
            if (!Directory.Exists(userData)) continue;

            yield return new FolderCleanupProvider(id, name,
                "Cached web pages, scripts and images. Cookies, history and passwords are kept.",
                CleanupGroup.Browsers, SymbolRegular.Globe24,
                () => ChromiumTargets(userData))
            { BlockingProcesses = [process] };
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
