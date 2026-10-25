using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PCCleaner.Models;
using PCCleaner.Services.Native;

namespace PCCleaner.Services;

/// <summary>
/// Lists programs that start with Windows and enables/disables them through the StartupApproved
/// registry keys — the same mechanism Task Manager uses, so nothing is ever deleted.
/// </summary>
public sealed class StartupService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public Task<IReadOnlyList<StartupItem>> GetItemsAsync() => Task.Run<IReadOnlyList<StartupItem>>(() =>
    {
        var items = new List<StartupItem>();
        items.AddRange(ReadRunKey(RegistryHive.CurrentUser, RunKey, "Run", "Registry (current user)"));
        items.AddRange(ReadRunKey(RegistryHive.LocalMachine, RunKey, "Run", "Registry (all users)"));
        items.AddRange(ReadRunKey(RegistryHive.LocalMachine, Run32Key, "Run32", "Registry (all users, 32-bit)"));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Startup folder (current user)"));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Startup folder (all users)"));
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    public void SetEnabled(StartupItem item, bool enabled)
    {
        using var root = RegistryKey.OpenBaseKey(item.ApprovedHive, RegistryView.Registry64);
        using var key = root.CreateSubKey(item.ApprovedKeyPath, writable: true);

        // Byte 0: 0x02 = enabled, 0x03 = disabled. Bytes 4-11: FILETIME when it was disabled.
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);

        key.SetValue(item.EntryName, data, RegistryValueKind.Binary);
        item.IsEnabled = enabled;
    }

    public static void OpenFileLocation(StartupItem item)
    {
        var path = item.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private static IEnumerable<StartupItem> ReadRunKey(RegistryHive hive, string keyPath, string approvedSubKey, string location)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(keyPath);
        if (key is null) yield break;

        var approvedPath = ApprovedRoot + approvedSubKey;
        foreach (var valueName in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(valueName)) continue;
            if (key.GetValue(valueName) is not string command || string.IsNullOrWhiteSpace(command)) continue;

            var exe = ExtractExecutable(command);
            yield return Create(valueName, command, exe, location, hive, approvedPath);
        }
    }

    private static IEnumerable<StartupItem> ReadStartupFolder(string folder, RegistryHive hive, string location)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) ?? file : file;
            yield return Create(fileName, target, target, location, hive, ApprovedRoot + "StartupFolder",
                displayFallback: Path.GetFileNameWithoutExtension(file), iconSource: file);
        }
    }

    private static StartupItem Create(string entryName, string command, string? exe, string location,
        RegistryHive hive, string approvedPath, string? displayFallback = null, string? iconSource = null)
    {
        FileVersionInfo? info = null;
        if (exe is not null && File.Exists(exe))
        {
            try { info = FileVersionInfo.GetVersionInfo(exe); } catch (FileNotFoundException) { }
        }

        var description = info?.FileDescription?.Trim();
        return new StartupItem
        {
            Name = string.IsNullOrEmpty(description) ? displayFallback ?? entryName : description,
            EntryName = entryName,
            Command = command,
            Location = location,
            FilePath = exe,
            Publisher = string.IsNullOrWhiteSpace(info?.CompanyName) ? null : info.CompanyName.Trim(),
            Icon = LoadIcon(iconSource ?? exe),
            IsEnabled = ReadApproved(hive, approvedPath, entryName),
            IsMachineWide = hive == RegistryHive.LocalMachine,
            ApprovedHive = hive,
            ApprovedKeyPath = approvedPath,
        };
    }

    private static bool ReadApproved(RegistryHive hive, string approvedPath, string entryName)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(approvedPath);
        // No value means enabled; an odd first byte means disabled.
        return key?.GetValue(entryName) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
    }

    /// <summary>Gets the program path out of a command line like <c>"C:\x\app.exe" --min</c> or <c>C:\x\app.exe /s</c>.</summary>
    internal static string? ExtractExecutable(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0) return command[..(exeIndex + 4)];

        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var target = (string)shell.CreateShortcut(lnkPath).TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static ImageSource? LoadIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        var info = new NativeMethods.SHFILEINFO();
        var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);
        if (NativeMethods.SHGetFileInfo(path, 0, ref info, size, NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }
}
