<#
.SYNOPSIS
    Builds Ultimate PC Cleaner end to end: runs tests, publishes a self-contained x64 build and compiles the Inno Setup installer.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests
.NOTES
    Requires the .NET 10 SDK and Inno Setup 6 (winget install --id JRSoftware.InnoSetup -e).
    Output: artifacts\installer\UltimatePCCleanerSetup-<version>.exe
#>
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\PCCleaner\PCCleaner.csproj'
$publishDir = Join-Path $root 'artifacts\publish'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "Ultimate PC Cleaner $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host "`n==> Running tests" -ForegroundColor Cyan
    dotnet test (Join-Path $root 'PCCleaner.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

Write-Host "`n==> Publishing self-contained win-x64 build" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# Single-file keeps the install folder tidy; native WPF DLLs stay next to the exe so nothing is extracted to %TEMP%.
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Write-Host "`n==> Compiling installer" -ForegroundColor Cyan
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) + (Get-ChildItem "$env:LOCALAPPDATA\Programs", $env:ProgramFiles, ${env:ProgramFiles(x86)} -Filter 'ISCC.exe' -Recurse -Depth 2 -ErrorAction SilentlyContinue | ForEach-Object FullName) |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) { throw 'Inno Setup (ISCC.exe) not found. Install it with: winget install --id JRSoftware.InnoSetup -e' }

& $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" (Join-Path $root 'installer\UltimatePCCleaner.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

$setup = Join-Path $root "artifacts\installer\UltimatePCCleanerSetup-$version.exe"
Write-Host "`nInstaller ready: $setup" -ForegroundColor Green
