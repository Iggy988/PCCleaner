# PC Cleaner

A Windows 10/11 cleaner and optimizer built with WPF (.NET 10) and the [WPF-UI](https://github.com/lepoco/wpfui) Fluent design library.

## Features

| Page | What it does |
|---|---|
| **Dashboard** | Live CPU / RAM / disk gauges, system info, junk summary and a **1-Click Boost** (clean selected junk + flush DNS + free RAM). |
| **Cleaner** | Check or uncheck categories, **Scan** to see sizes, then **Clean selected**. Covers user and Windows temp files, the Windows Update and Delivery Optimization caches, error reports, memory dumps, old logs, the Recycle Bin, Prefetch, thumbnail cache, browser caches (Edge, Chrome, Brave, Firefox) and the DirectX shader cache. |
| **Startup apps** | Enable or disable programs that start with Windows. It uses the same mechanism as Task Manager, so nothing is deleted. |
| **Optimize** | Reversible switches for a High or Ultimate performance power plan, performance visual effects and turning off Game DVR. Also has quick actions: Flush DNS, Free RAM, Create restore point. |
| **Health** | Runs SFC, DISM RestoreHealth, DISM component cleanup, drive optimization (TRIM/defrag) and CHKDSK, with live output. |
| **Settings** | Theme (System, Light or Dark), skip recent temp files, remember selections, restore point before tweaks. |

### Safety

- Never follows junctions or symlinks.
- Never deletes a category's root folder.
- Files that are in use are skipped and counted.
- By default, temp files newer than 24 hours are left alone.
- Browser cleaning removes **cache only**. Cookies, history and passwords are kept.
- A System Restore point is created before the first tweak in each session.
- Every tweak can be reverted.
- Logs: `%LOCALAPPDATA%\PCCleaner\logs`

## Build the installer

Prerequisites: the .NET 10 SDK and Inno Setup 6 (`winget install --id JRSoftware.InnoSetup -e`).

```powershell
.\build.ps1            # tests -> self-contained publish -> installer
.\build.ps1 -SkipTests
```

Output: `artifacts\installer\PCCleanerSetup-<version>.exe`. Copy that single file to any 64-bit Windows 10 (1809+) or Windows 11 PC and run it. The target PC does **not** need .NET installed.

To release a new version:
1. Bump `<Version>` in `src/PCCleaner/PCCleaner.csproj`.
2. Run `build.ps1` again. Installing over an existing copy upgrades it in place.

> The installer is not code-signed, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**. Signing it needs a code-signing certificate.

## Develop

```powershell
dotnet build
dotnet test
dotnet run --project src/PCCleaner
```

- **Debug builds** run as your normal user. Admin-only items show "Requires admin", and you can use **Restart as administrator** to elevate.
- **Release builds** always request administrator rights.

### Project layout

```
src/PCCleaner/
  Services/Cleaning/   deletion engine (FileCleaner), cleanup categories (CleanupCatalog)
  Services/Tweaks/     reversible optimizations (ITweak)
  Services/            startup manager, restore points, health tools, system info, settings
  ViewModels/          MVVM (CommunityToolkit.Mvvm)
  Views/               FluentWindow + pages
  Controls/ArcGauge    animated circular gauge
tests/PCCleaner.Tests/ xUnit tests for the deletion engine and helpers
installer/             Inno Setup script
tools/make-icon.ps1    regenerates Assets/app.ico and app.png
```
