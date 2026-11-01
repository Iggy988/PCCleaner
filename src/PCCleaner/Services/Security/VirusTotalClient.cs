using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCCleaner.Services.Security;

/// <summary>
/// Opt-in hash lookup on VirusTotal (API v3). Only the file's SHA-256 is sent, never the file. The free API allows
/// 4 requests per minute, so calls are throttled and answers cached for the session.
/// </summary>
public sealed class VirusTotalClient(SettingsService settings)
{
    private const int RequestsPerMinute = 4;

    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://www.virustotal.com/api/v3/"), Timeout = TimeSpan.FromSeconds(30) };

    private readonly SemaphoreSlim _throttle = new(1, 1);
    private readonly Queue<DateTime> _recent = new();
    private readonly Dictionary<string, int> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled => settings.Current.VirusTotalEnabled && !string.IsNullOrEmpty(GetApiKey());

    /// <summary>The API key, stored encrypted for the current Windows user (DPAPI).</summary>
    public string? GetApiKey()
    {
        var stored = settings.Current.VirusTotalApiKeyProtected;
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public void SetApiKey(string? apiKey)
    {
        settings.Current.VirusTotalApiKeyProtected = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser));
        settings.Save();
    }

    /// <summary>Number of engines flagging the file as malicious or suspicious; -1 if VirusTotal doesn't know it.</summary>
    public async Task<int> LookupFileAsync(string path, CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey() ?? throw new InvalidOperationException("No VirusTotal API key.");
        var hash = await ComputeSha256Async(path, cancellationToken);
        lock (_cache)
        {
            if (_cache.TryGetValue(hash, out var cached)) return cached;
        }

        await WaitForSlotAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"files/{hash}");
        request.Headers.Add("x-apikey", apiKey);
        using var response = await Http.SendAsync(request, cancellationToken);

        int positives;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            positives = -1;
        }
        else
        {
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            positives = ParsePositives(await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken));
        }

        lock (_cache) _cache[hash] = positives;
        AppLog.Write($"VirusTotal: {hash} -> {positives}");
        return positives;
    }

    internal static int ParsePositives(JsonDocument document)
    {
        var stats = document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("last_analysis_stats");
        return stats.GetProperty("malicious").GetInt32() + stats.GetProperty("suspicious").GetInt32();
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            while (_recent.Count > 0 && DateTime.UtcNow - _recent.Peek() > TimeSpan.FromMinutes(1)) _recent.Dequeue();
            if (_recent.Count >= RequestsPerMinute)
            {
                var wait = _recent.Peek().AddMinutes(1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
                _recent.Dequeue();
            }

            _recent.Enqueue(DateTime.UtcNow);
        }
        finally
        {
            _throttle.Release();
        }
    }
}
