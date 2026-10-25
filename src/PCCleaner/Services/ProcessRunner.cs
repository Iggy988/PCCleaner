using System.Diagnostics;
using System.Text;

namespace PCCleaner.Services;

public static class ProcessRunner
{
    /// <summary>Runs a console tool and streams its output line by line. Kills the process tree on cancellation.</summary>
    public static async Task<int> RunAsync(string fileName, string arguments, Action<string> onLine,
        Encoding? outputEncoding, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        return process.ExitCode;
    }

    /// <summary>Runs a short command synchronously and returns its exit code and combined output.</summary>
    public static (int ExitCode, string Output) Run(string fileName, string arguments, int timeoutMs = 30_000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "Timed out");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
