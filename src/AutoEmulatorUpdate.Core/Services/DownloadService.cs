using System.Security.Cryptography;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class DownloadService(HttpClient http, AppPaths paths)
{
    public async Task<string> DownloadAsync(
        string emulatorId,
        ReleaseInfo release,
        double maxMBps,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var safeVersion = string.Concat(release.Version.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dir = Path.Combine(paths.CacheRoot, emulatorId, safeVersion);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, release.AssetName);
        if (File.Exists(target) && await VerifyAsync(target, release.Sha256, ct)) return target;

        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        var buffer = new byte[128 * 1024];
        long received = 0;
        var started = DateTime.UtcNow;

        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            if (total > 0) progress?.Report(received * 100d / total.Value);

            if (maxMBps > 0)
            {
                var expected = received / (maxMBps * 1024d * 1024d);
                var actual = (DateTime.UtcNow - started).TotalSeconds;
                if (expected > actual)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(.5, expected - actual)), ct);
            }
        }

        if (!await VerifyAsync(target, release.Sha256, ct))
            throw new InvalidDataException("Downloaded package failed SHA-256 verification.");
        return target;
    }

    public static async Task<bool> VerifyAsync(string path, string? expectedSha256, CancellationToken ct = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        var actual = Convert.ToHexString(hash);
        return actual.Equals(expectedSha256.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
    }
}
