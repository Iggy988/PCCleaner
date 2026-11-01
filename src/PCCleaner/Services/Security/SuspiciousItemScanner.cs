using System.IO;
using System.Xml;
using System.Xml.Linq;
using PCCleaner.Models;
using PCCleaner.Services.Cleaning;
using PCCleaner.Services.Localization;

namespace PCCleaner.Services.Security;

/// <summary>
/// Looks for startup entries, scheduled tasks and scripts that behave like malware (see <see cref="SuspiciousRules"/>).
/// It only reports; quarantining is a separate, user-confirmed step.
/// </summary>
public sealed class SuspiciousItemScanner(StartupService startup, AmsiScanner amsi)
{
    private const int MaxScripts = 5000;

    public async Task<IReadOnlyList<SuspiciousItem>> ScanAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var roots = SuspiciousRules.DefaultUserWritableRoots();
        var ownExe = Environment.ProcessPath;
        var results = new List<SuspiciousItem>();

        progress?.Report(Loc.T("Sec_Progress_Startup"));
        var startupItems = await startup.GetItemsAsync();

        await Task.Run(() =>
        {
            foreach (var item in startupItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ResolveExecutable(item.FilePath);
                if (IsSamePath(path, ownExe)) continue;
                Add(results, item.Name, SuspiciousSource.StartupEntry, item.Command, path, roots, item);
            }

            progress?.Report(Loc.T("Sec_Progress_Tasks"));
            foreach (var (name, command, executable) in ReadScheduledTasks())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ResolveExecutable(executable);
                if (IsSamePath(path, ownExe)) continue;
                Add(results, name, SuspiciousSource.ScheduledTask, command, path, roots, null);
            }

            progress?.Report(Loc.T("Sec_Progress_Scripts"));
            ScanScripts(results, roots, cancellationToken);
        }, cancellationToken);

        return results
            .OrderByDescending(r => r.Severity)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void Add(List<SuspiciousItem> results, string name, SuspiciousSource source, string? command, string? path,
        IReadOnlyList<string> roots, StartupItem? startupItem)
    {
        var exists = path is not null && File.Exists(path);
        // Only files a user could have dropped are worth a signature check; system folders are catalog-signed.
        var signature = exists && SuspiciousRules.IsInUserWritableLocation(path!, roots) ? Authenticode.Check(path!) : SignatureState.Unknown;
        var amsiVerdict = exists && AmsiScanner.ScriptExtensions.Contains(Path.GetExtension(path!)) ? amsi.ScanFile(path!) : AmsiVerdict.NotDetected;

        var (reasons, severity) = SuspiciousRules.Evaluate(new Evidence(command, path, exists, false, signature, amsiVerdict), roots);
        if (reasons.Count == 0) return;

        results.Add(new SuspiciousItem
        {
            DisplayName = name,
            Source = source,
            FilePath = path,
            Command = command,
            Reasons = reasons,
            Severity = severity,
            StartupItem = startupItem,
        });
    }

    private void ScanScripts(List<SuspiciousItem> results, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 1,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", options)
                    .Where(f => AmsiScanner.ScriptExtensions.Contains(Path.GetExtension(f)))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;
                if (seen.Count > MaxScripts) return;

                string content;
                try
                {
                    content = AmsiScanner.ReadText(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var verdict = amsi.ScanText(content, file);
                var (reasons, severity) = SuspiciousRules.Evaluate(
                    new Evidence(null, file, true, true, SignatureState.Unknown, verdict, content), roots);
                if (reasons.Count == 0) continue;

                results.Add(new SuspiciousItem
                {
                    DisplayName = Path.GetFileName(file),
                    Source = SuspiciousSource.ScriptFile,
                    FilePath = file,
                    Reasons = reasons,
                    Severity = severity,
                });
            }
        }
    }

    /// <summary>Exec actions of all scheduled tasks, from <c>schtasks /query /xml ONE</c> (task names are XML comments).</summary>
    private static IEnumerable<(string Name, string Command, string Executable)> ReadScheduledTasks()
    {
        var (code, output) = ProcessRunner.Run(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/query /xml ONE", 60_000);
        var start = output.IndexOf('<');
        if (code != 0 || start < 0) return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(output[start..]);
        }
        catch (XmlException ex)
        {
            AppLog.Write($"Could not parse scheduled tasks: {ex.Message}");
            return [];
        }

        var tasks = new List<(string, string, string)>();
        string? name = null;
        foreach (var node in document.Root?.Nodes() ?? [])
        {
            if (node is XComment comment)
            {
                name = comment.Value.Trim();
                continue;
            }

            if (node is not XElement task) continue;
            var ns = task.Name.Namespace;
            if (string.Equals(task.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var exec in task.Descendants(ns + "Exec"))
            {
                var command = exec.Element(ns + "Command")?.Value.Trim().Trim('"');
                if (string.IsNullOrEmpty(command)) continue;
                var arguments = exec.Element(ns + "Arguments")?.Value.Trim();
                tasks.Add((name ?? command, $"\"{command}\" {arguments}".TrimEnd(), command));
            }
        }

        return tasks;
    }

    /// <summary>Expands variables and resolves bare names like "rundll32.exe" against System32. Null if unknown.</summary>
    internal static string? ResolveExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (Path.IsPathFullyQualified(path)) return path;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains('\\')) return null;

        var system = Path.Combine(Environment.SystemDirectory, Path.HasExtension(path) ? path : path + ".exe");
        return File.Exists(system) ? system : null;
    }

    private static bool IsSamePath(string? a, string? b) =>
        a is not null && b is not null && string.Equals(PathSafety.Normalize(a), PathSafety.Normalize(b), StringComparison.OrdinalIgnoreCase);
}
