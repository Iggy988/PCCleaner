# Ultimate PC Cleaner

A Windows 10/11 cleaner and optimizer built with WPF (.NET 10) and the [WPF-UI](https://github.com/lepoco/wpfui) Fluent design library.

## Features

| Page | What it does |
|---|---|
| **Dashboard** | Animated overview: live CPU / RAM / disk gauges, counters for junk found, temporary files, total space freed and last clean, plus a **1-Click Boost** (clean selected junk + flush DNS + free RAM, after confirmation). |
| **Temp files** | Scans `%LOCALAPPDATA%\Temp`, `%WINDIR%\Temp` and `%WINDIR%\Prefetch` (`*.pf` only). Shows eligible files, reclaimable space, files in use and inaccessible items per location. You pick locations, confirm, then watch live progress. The result reports what was deleted, skipped (and why) or already gone. |
| **Cleaner** | Check or uncheck categories, **Scan** to see sizes, then **Clean selected**. Covers user and Windows temp files, the Windows Update and Delivery Optimization caches, error reports, memory dumps, old logs, the Recycle Bin, Prefetch, thumbnail cache, browser caches (Edge, Chrome, Brave, Firefox) and the DirectX shader cache. |
| **Startup apps** | Enable or disable programs that start with Windows. It uses the same mechanism as Task Manager, so nothing is deleted. |
| **Security** | Security scan powered by Microsoft Defender. It's a front-end, not an antivirus of its own. Shows protection status; runs quick, full and folder scans and signature updates; lists Defender's detections (remove active threats, or allow one). **Check scripts (AMSI)** asks whichever antivirus is installed about every script in a folder. **Find suspicious items** flags startup entries, scheduled tasks and scripts with hidden/encoded commands, disguised names (`invoice.pdf.exe`), unsigned programs in user-writable folders, or an AMSI detection; optional VirusTotal hash lookup. Flagged files can be moved to the app's own quarantine and restored later, and Defender's quarantine can be restored from here too. |
| **Optimize** | Reversible switches for a High or Ultimate performance power plan, performance visual effects and turning off Game DVR. Also has quick actions: Flush DNS, Free RAM, Create restore point. |
| **Health** | Runs SFC, DISM RestoreHealth, DISM component cleanup, drive optimization (TRIM/defrag) and CHKDSK, with live output. |
| **User manual** | Built-in guide: a quick start, a section for every page (what it does and how to use it, with an **Open page** shortcut), the safety rules, what needs administrator rights and an FAQ. |
| **Settings** | Language, theme (System, Light or Dark), reduce animations, skip recent temp files, remember selections, restore point before tweaks, opt-in VirusTotal lookup (API key stored with DPAPI). |

### Languages

Serbian (Latin) is the default. English, Russian, German and Chinese (Simplified) can be chosen in **Settings**, and the change applies immediately. Numbers, dates and file sizes follow the selected language. Strings live in `src/PCCleaner/Resources/Strings/Strings*.resx` (`Strings.resx` is the English fallback). For a one-off session in another language, start the app with `--language=ru` (or `en`, `de`, `zh-Hans`, `sr-Latn`); this doesn't change the saved setting.

### Safety

- Paths are resolved from Windows environment variables (`%LOCALAPPDATA%`, `%WINDIR%`), never hardcoded. A path that can't be resolved safely (relative, UNC, drive root, `..`, or itself a link) is reported and never cleaned.
- Only files are deleted. A location's root folder is never removed; empty sub-folders are removed one at a time, never recursively.
- Junctions and symlinks are never followed.
- Every file is re-checked immediately before deletion. It must still exist, still be inside the target, still match the pattern and age rules, and not be reached through a link.
- Files in use, without permission, or that fail the re-check are skipped. Cleaning continues, and the result lists them, so partial success is reported as partial.
- By default, temp files newer than 24 hours are left alone.
- Browser cleaning removes **cache only**. Cookies, history and passwords are kept.
- Every destructive action asks for confirmation first, including Boost.
- Clearing Prefetch is opt-in. Windows manages it automatically: clearing it does not make the PC faster, and apps may start more slowly at first while it is rebuilt.
- A System Restore point is created before the first tweak in each session, and every tweak can be reverted.
- The Security page never deletes anything on its own. Suspicious items are only reported. Quarantine is user-confirmed and stores files non-executable (XOR-encoded) in `%ProgramData%\UltimatePCCleaner\Quarantine` (Administrators and SYSTEM only), with metadata for an exact restore. A quarantined startup entry is disabled, not deleted. Files under `%WINDIR%` can't be quarantined. VirusTotal receives only SHA-256 hashes, and only when you turn it on.
- Logs: `%LOCALAPPDATA%\UltimatePCCleaner\logs`. Settings from the old `%LOCALAPPDATA%\PCCleaner` folder are migrated automatically.

### Administrator rights

The app runs as the current user. Your own Temp folder, browser caches, startup apps and most tweaks work without elevation. Windows Temp, Prefetch, system caches, services, restore points, the health tools and Defender scans/quarantine need administrator rights. The app explains this where it applies and offers **Restart as administrator** (UAC) only when you ask for it.

## Build the installer

Prerequisites: the .NET 10 SDK and Inno Setup 6 (`winget install --id JRSoftware.InnoSetup -e`).

```powershell
.\build.ps1            # tests -> self-contained publish -> installer
.\build.ps1 -SkipTests
```

Output: `artifacts\installer\UltimatePCCleanerSetup-<version>.exe`. Copy that single file to any 64-bit Windows 10 (1809+) or Windows 11 PC and run it. The target PC does **not** need .NET installed. The setup wizard is available in Serbian (default), English, Russian, German and Chinese (Simplified). Installing over PC Cleaner 1.x upgrades it in place.

To release a new version:
1. Bump `<Version>` in `src/PCCleaner/PCCleaner.csproj`.
2. Run `build.ps1` again.

> The installer is not code-signed, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**. Signing it needs a code-signing certificate.

## Develop

```powershell
dotnet build
dotnet test
dotnet run --project src/PCCleaner
```

### Project layout

```
src/PCCleaner/
  Services/Cleaning/       deletion engine (FileCleaner), path checks (PathSafety), cleanup categories (CleanupCatalog)
  Services/Cleaning/Temp/  temp-location resolver and Temp Files cleaner
  Services/Localization/   LocalizationService, {loc:Tr Key} markup extension
  Services/Security/       Defender (WMI + MpCmdRun), AMSI scanner, suspicious-item rules and scanner, quarantine, VirusTotal
  Services/Tweaks/         reversible optimizations (ITweak)
  Services/                startup manager, restore points, health tools, system info, settings
  Resources/Strings/       UI strings (en, sr-Latn, ru, de, zh-Hans)
  ViewModels/              MVVM (CommunityToolkit.Mvvm)
  Views/                   FluentWindow + pages
  Controls/                ArcGauge, AnimatedNumber, Motion (entrance/hover animations, reduced-motion aware)
tests/PCCleaner.Tests/     xUnit tests: deletion engine, path safety, temp locations, localization, suspicious-item rules, quarantine
installer/                 Inno Setup script, wizard images, extra language files
tools/make-icon.ps1        regenerates app.ico, app.png, logo.png and wizard images from Assets/ultimate_cleaner.png
```
