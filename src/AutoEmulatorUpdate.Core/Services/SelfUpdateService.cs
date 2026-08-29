using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record AppUpdateInfo(string Version, string DownloadUrl, string AssetName, string ReleaseUrl);

public sealed class SelfUpdateService(HttpClient http)
{
    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!AutoEmulatorUpdate.Core.BuildInfo.HasConfiguredRepository) return null;

        var repo = AutoEmulatorUpdate.Core.BuildInfo.GitHubRepository;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.UserAgent.ParseAdd("AutoEmulatorUpdate/10.1");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(tag) || tag == AutoEmulatorUpdate.Core.BuildInfo.Version) return null;

        var desired = DesiredAssetHints();
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (desired.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                return new AppUpdateInfo(
                    tag,
                    asset.GetProperty("browser_download_url").GetString()!,
                    name,
                    root.GetProperty("html_url").GetString()!);
            }
        }

        return new AppUpdateInfo(tag, root.GetProperty("html_url").GetString()!, "Release page", root.GetProperty("html_url").GetString()!);
    }

    private static string[] DesiredAssetHints()
    {
        if (OperatingSystem.IsWindows()) return ["Setup.exe", "win-x64", "win-arm64"];
        if (OperatingSystem.IsMacOS()) return RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? ["macOS-arm64", "osx-arm64", ".dmg"]
            : ["macOS-x64", "osx-x64", ".dmg"];
        return RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? ["linux-arm64", "AppImage"]
            : ["linux-x64", "AppImage"];
    }
}
