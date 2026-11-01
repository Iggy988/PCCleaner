using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Native;

namespace PCCleaner.Services.Security;

/// <summary>
/// The app's own quarantine in %ProgramData%\UltimatePCCleaner\Quarantine (Administrators and SYSTEM only).
/// Files are stored XOR-encoded as <c>{id}.qbin</c> with their metadata in <c>{id}.json</c>, so they can't run
/// and can be restored byte-for-byte. Startup entries pointing at them are disabled, never deleted.
/// </summary>
public sealed class QuarantineService
{
    private const int BufferSize = 81920;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly StartupService _startup;
    private readonly bool _restrictAccess;

    public QuarantineService(StartupService startup)
        : this(startup, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UltimatePCCleaner", "Quarantine"), restrictAccess: true)
    {
    }

    /// <summary>Tests use a temporary root and skip the admin-only ACL.</summary>
    internal QuarantineService(StartupService startup, string root, bool restrictAccess)
    {
        _startup = startup;
        Root = root;
        _restrictAccess = restrictAccess;
    }

    public string Root { get; }

    public IReadOnlyList<QuarantineEntry> GetEntries()
    {
        if (!Directory.Exists(Root)) return [];

        var entries = new List<QuarantineEntry>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<QuarantineEntry>(File.ReadAllText(file), JsonOptions);
                if (entry is not null && File.Exists(DataPath(entry.Id))) entries.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Write($"Unreadable quarantine entry {file}: {ex.Message}");
            }
        }

        return entries.OrderByDescending(e => e.QuarantinedAt).ToList();
    }

    public Task<QuarantineEntry> QuarantineAsync(string path, IReadOnlyList<string> reasons, StartupItem? startupItem) => Task.Run(() =>
    {
        var source = new FileInfo(PathSafety.Normalize(path));
        if (!source.Exists) throw new FileNotFoundException(null, source.FullName);
        if (PathSafety.IsStrictlyInside(source.FullName, Root) || PathSafety.IsStrictlyInside(source.FullName, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
            throw new UnauthorizedAccessException(source.FullName);

        EnsureRoot();
        var entry = new QuarantineEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            OriginalPath = source.FullName,
            Size = source.Length,
            QuarantinedAt = DateTime.Now,
            Reasons = reasons.ToList(),
            Attributes = source.Attributes,
            CreationTimeUtc = source.CreationTimeUtc,
            LastWriteTimeUtc = source.LastWriteTimeUtc,
            StartupHive = startupItem?.ApprovedHive,
            StartupKeyPath = startupItem?.ApprovedKeyPath,
            StartupEntryName = startupItem?.EntryName,
            StartupWasEnabled = startupItem?.IsEnabled == true,
        };

        var key = RandomNumberGenerator.GetBytes(32);
        entry.Key = Convert.ToBase64String(key);

        try
        {
            entry.Sha256 = Transform(source.FullName, DataPath(entry.Id), key, fromQuarantine: false);
            SaveEntry(entry);

            if (!TryDelete(source.FullName))
            {
                // Locked (e.g. the program is running): delete it on the next restart instead.
                if (!NativeMethods.MoveFileEx(source.FullName, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT))
                    throw new IOException($"Could not remove {source.FullName} (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
                entry.PendingReboot = true;
                SaveEntry(entry);
            }
        }
        catch
        {
            TryDelete(DataPath(entry.Id));
            TryDelete(EntryPath(entry.Id));
            throw;
        }

        if (startupItem is { IsEnabled: true }) _startup.SetEnabled(startupItem, false);
        AppLog.Write($"Quarantined {entry.OriginalPath} as {entry.Id} ({string.Join(", ", entry.Reasons)})");
        return entry;
    });

    public Task<QuarantineRestoreResult> RestoreAsync(string id, bool overwrite) => Task.Run(() =>
    {
        var entry = LoadEntry(id);
        if (entry.PendingReboot && File.Exists(entry.OriginalPath)) return QuarantineRestoreResult.PendingReboot;
        if (File.Exists(entry.OriginalPath) && !overwrite) return QuarantineRestoreResult.TargetExists;

        Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
        var temp = entry.OriginalPath + ".restore-" + entry.Id[..8];
        TryDelete(temp);
        var hash = Transform(DataPath(id), temp, Convert.FromBase64String(entry.Key), fromQuarantine: true);
        if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(temp);
            throw new InvalidDataException($"Quarantined file {id} is damaged (hash mismatch).");
        }

        File.Move(temp, entry.OriginalPath, overwrite);
        File.SetCreationTimeUtc(entry.OriginalPath, entry.CreationTimeUtc);
        File.SetLastWriteTimeUtc(entry.OriginalPath, entry.LastWriteTimeUtc);
        File.SetAttributes(entry.OriginalPath, entry.Attributes);

        if (entry is { StartupWasEnabled: true, StartupHive: { } hive, StartupKeyPath: { } keyPath, StartupEntryName: { } name })
        {
            _startup.SetEnabled(new StartupItem
            {
                Name = name,
                EntryName = name,
                Command = entry.OriginalPath,
                Location = "",
                ApprovedHive = hive,
                ApprovedKeyPath = keyPath,
            }, true);
        }

        TryDelete(DataPath(id));
        TryDelete(EntryPath(id));
        AppLog.Write($"Restored {entry.OriginalPath} from quarantine");
        return QuarantineRestoreResult.Restored;
    });

    public Task DeletePermanentlyAsync(string id) => Task.Run(() =>
    {
        File.Delete(DataPath(id));
        File.Delete(EntryPath(id));
        AppLog.Write($"Deleted quarantined item {id}");
    });

    /// <summary>Copies <paramref name="from"/> to <paramref name="to"/> XOR-ed with <paramref name="key"/>; returns the SHA-256 of the plain content.</summary>
    private static string Transform(string from, string to, byte[] key, bool fromQuarantine)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write))
        {
            var buffer = new byte[BufferSize];
            long position = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (!fromQuarantine) hash.AppendData(buffer, 0, read);
                for (var i = 0; i < read; i++) buffer[i] ^= key[(position + i) % key.Length];
                if (fromQuarantine) hash.AppendData(buffer, 0, read);
                output.Write(buffer, 0, read);
                position += read;
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private void EnsureRoot()
    {
        var created = !Directory.Exists(Root);
        Directory.CreateDirectory(Root);
        if (!_restrictAccess || !created) return;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(Root).SetAccessControl(security);
    }

    private QuarantineEntry LoadEntry(string id) =>
        JsonSerializer.Deserialize<QuarantineEntry>(File.ReadAllText(EntryPath(id)), JsonOptions)
        ?? throw new InvalidDataException($"Quarantine entry {id} is empty.");

    private void SaveEntry(QuarantineEntry entry) =>
        File.WriteAllText(EntryPath(entry.Id), JsonSerializer.Serialize(entry, JsonOptions));

    private string DataPath(string id) => Path.Combine(Root, id + ".qbin");

    private string EntryPath(string id) => Path.Combine(Root, id + ".json");

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
