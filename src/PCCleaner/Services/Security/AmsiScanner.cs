using System.IO;
using System.Text;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Native;

namespace PCCleaner.Services.Security;

/// <summary>
/// Asks the registered antivirus (Defender, ESET, Kaspersky...) about script content through the Windows
/// Antimalware Scan Interface. Returns <see cref="AmsiVerdict.Unavailable"/> when no provider answers.
/// </summary>
public sealed class AmsiScanner : IDisposable
{
    private const int MaxBytes = 4 * 1024 * 1024;

    public static readonly IReadOnlySet<string> ScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".bat", ".cmd",
    };

    private readonly Lock _gate = new();
    private IntPtr _context;
    private bool _initFailed;

    public AmsiVerdict ScanFile(string path)
    {
        string text;
        try
        {
            text = ReadText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AmsiVerdict.Unavailable;
        }

        return ScanText(text, path);
    }

    public AmsiVerdict ScanText(string text, string contentName)
    {
        var context = GetContext();
        if (context == IntPtr.Zero) return AmsiVerdict.Unavailable;

        if (NativeMethods.AmsiOpenSession(context, out var session) != 0) return AmsiVerdict.Unavailable;
        try
        {
            if (NativeMethods.AmsiScanString(context, text, contentName, session, out var result) != 0)
                return AmsiVerdict.Unavailable;

            return result switch
            {
                >= NativeMethods.AMSI_RESULT_DETECTED => AmsiVerdict.Malware,
                >= NativeMethods.AMSI_RESULT_BLOCKED_BY_ADMIN_START and <= NativeMethods.AMSI_RESULT_BLOCKED_BY_ADMIN_END => AmsiVerdict.BlockedByAdmin,
                0 => AmsiVerdict.Clean,
                _ => AmsiVerdict.NotDetected,
            };
        }
        finally
        {
            NativeMethods.AmsiCloseSession(context, session);
        }
    }

    /// <summary>Scans the script files in <paramref name="folder"/> and its subfolders; reports each file that AMSI flags.</summary>
    public Task<IReadOnlyList<string>> ScanFolderAsync(string folder, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var detected = new List<string>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ScriptExtensions.Contains(Path.GetExtension(file)) || PathSafety.HasReparsePointBetween(file, folder)) continue;

                var verdict = ScanFile(file);
                progress?.Report($"{verdict,-14} {file}");
                if (verdict == AmsiVerdict.Malware) detected.Add(file);
            }

            return detected;
        }, cancellationToken);

    /// <summary>Reads up to 4 MB of a script, honouring a UTF-8/UTF-16 byte-order mark.</summary>
    internal static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[(int)Math.Min(stream.Length, MaxBytes)];
        stream.ReadExactly(buffer);
        using var reader = new StreamReader(new MemoryStream(buffer), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private IntPtr GetContext()
    {
        lock (_gate)
        {
            if (_context != IntPtr.Zero || _initFailed) return _context;
            try
            {
                if (NativeMethods.AmsiInitialize("UltimatePCCleaner", out var context) == 0) _context = context;
                else _initFailed = true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _initFailed = true;
            }

            if (_initFailed) AppLog.Write("AMSI is not available on this system.");
            return _context;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_context == IntPtr.Zero) return;
            NativeMethods.AmsiUninitialize(_context);
            _context = IntPtr.Zero;
        }
    }
}
