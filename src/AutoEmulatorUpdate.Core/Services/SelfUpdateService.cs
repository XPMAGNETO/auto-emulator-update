using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record AppUpdateInfo(
    string Version,
    string DownloadUrl,
    string AssetName,
    string ReleaseUrl,
    string? ChecksumUrl = null,
    string? ChecksumAssetName = null);

public sealed record AppUpdateDownload(string FilePath, string Version, string AssetName, bool ChecksumVerified);

public sealed class SelfUpdateService(HttpClient http)
{
    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!AutoEmulatorUpdate.Core.BuildInfo.HasConfiguredRepository) return null;

        var repo = AutoEmulatorUpdate.Core.BuildInfo.GitHubRepository;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=20");
        req.Headers.UserAgent.ParseAdd("AutoEmulatorUpdate/10.1");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

        JsonElement? chosen = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var prerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if (!ShouldConsiderRelease(prerelease, AutoEmulatorUpdate.Core.BuildInfo.Version)) continue;
            var tag = release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(tag)) continue;
            if (CompareVersions(tag, AutoEmulatorUpdate.Core.BuildInfo.Version) <= 0) continue;

            if (chosen is null)
            {
                chosen = release.Clone();
                continue;
            }

            var currentChosen = chosen.Value.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "0";
            if (CompareVersions(tag, currentChosen) > 0)
                chosen = release.Clone();
        }

        if (chosen is null) return null;
        var root = chosen.Value;
        var version = root.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
        var releaseUrl = root.GetProperty("html_url").GetString()!;
        var assets = root.GetProperty("assets").EnumerateArray().Select(x => new
        {
            Name = x.GetProperty("name").GetString() ?? "",
            Url = x.GetProperty("browser_download_url").GetString() ?? ""
        }).ToArray();

        var desired = DesiredAssetHints();
        var package = assets.FirstOrDefault(a => desired.Any(h => a.Name.Contains(h, StringComparison.OrdinalIgnoreCase)));
        if (package is null)
            return new AppUpdateInfo(version, releaseUrl, "Release page", releaseUrl);

        var checksumCandidates = new[]
        {
            package.Name + ".sha256",
            Path.ChangeExtension(package.Name, ".sha256"),
            Path.GetFileNameWithoutExtension(package.Name) + ".sha256"
        };
        var checksum = assets.FirstOrDefault(a => checksumCandidates.Contains(a.Name, StringComparer.OrdinalIgnoreCase));

        return new AppUpdateInfo(version, package.Url, package.Name, releaseUrl, checksum?.Url, checksum?.Name);
    }

    public async Task<AppUpdateDownload> DownloadAndVerifyAsync(
        AppUpdateInfo update,
        IProgress<(double percent, string message)>? progress = null,
        CancellationToken ct = default)
    {
        if (update.AssetName.Equals("Release page", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("No compatible self-update package was published for this platform.");

        var safeVersion = string.Concat(update.Version.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        var root = Path.Combine(Path.GetTempPath(), "AutoEmulatorUpdate", "updates", safeVersion);
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, Path.GetFileName(update.AssetName));
        var partial = destination + ".partial";

        progress?.Report((0, $"Downloading Auto Emulator Updater {update.Version}..."));
        using (var req = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl))
        {
            req.Headers.UserAgent.ParseAdd("AutoEmulatorUpdate/10.1");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength;
            await using var input = await res.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
            var buffer = new byte[128 * 1024];
            long readTotal = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, ct);
                if (read <= 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                readTotal += read;
                var pct = total is > 0 ? Math.Clamp(readTotal * 100d / total.Value, 0, 100) : 0;
                progress?.Report((pct, total is > 0
                    ? $"Downloading update... {pct:0}%"
                    : $"Downloading update... {readTotal / 1024d / 1024d:0.0} MB"));
            }
        }

        File.Move(partial, destination, true);
        var verified = false;
        if (!string.IsNullOrWhiteSpace(update.ChecksumUrl))
        {
            progress?.Report((100, "Verifying update checksum..."));
            using var checksumReq = new HttpRequestMessage(HttpMethod.Get, update.ChecksumUrl);
            checksumReq.Headers.UserAgent.ParseAdd("AutoEmulatorUpdate/10.1");
            using var checksumRes = await http.SendAsync(checksumReq, ct);
            checksumRes.EnsureSuccessStatusCode();
            var checksumText = await checksumRes.Content.ReadAsStringAsync(ct);
            var expected = checksumText.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(x => x.Length == 64 && x.All(Uri.IsHexDigit));
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidDataException("The published update checksum could not be read.");

            await using var fs = File.OpenRead(destination);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded update failed SHA-256 verification and was not started.");
            verified = true;
        }

        progress?.Report((100, verified ? "Update verified and ready to install." : "Update downloaded and ready to install."));
        return new AppUpdateDownload(destination, update.Version, update.AssetName, verified);
    }

    public Process StartInstaller(AppUpdateDownload update)
    {
        if (!File.Exists(update.FilePath))
            throw new FileNotFoundException("The staged update package is missing.", update.FilePath);

        if (OperatingSystem.IsWindows())
        {
            var current = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
            {
                var helper = Path.Combine(Path.GetDirectoryName(update.FilePath)!, "apply-update.ps1");
                var installer = EscapePowerShellSingleQuoted(update.FilePath);
                var app = EscapePowerShellSingleQuoted(current);
                var pid = Environment.ProcessId;
                var script = $"$ErrorActionPreference = 'Stop'\r\n" +
                             $"$installer = '{installer}'\r\n" +
                             $"$app = '{app}'\r\n" +
                             $"$pidToWaitFor = {pid}\r\n" +
                             "$deadline = (Get-Date).AddSeconds(10)\r\n" +
                             "while (Get-Process -Id $pidToWaitFor -ErrorAction SilentlyContinue) { if ((Get-Date) -ge $deadline) { Stop-Process -Id $pidToWaitFor -Force -ErrorAction SilentlyContinue; break }; Start-Sleep -Milliseconds 250 }\r\n" +
                             "$args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS')\r\n" +
                             "$p = Start-Process -FilePath $installer -ArgumentList $args -Wait -PassThru\r\n" +
                             "if ($p.ExitCode -eq 0 -and (Test-Path -LiteralPath $app)) { Start-Sleep -Milliseconds 750; Start-Process -FilePath $app }\r\n" +
                             "exit $p.ExitCode\r\n";
                File.WriteAllText(helper, script);
                return Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{helper}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }) ?? throw new InvalidOperationException("Windows could not start the update helper.");
            }

            return Process.Start(new ProcessStartInfo
            {
                FileName = update.FilePath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS",
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Windows could not start the update installer.");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Process.Start(new ProcessStartInfo("open", $"\"{update.FilePath}\"") { UseShellExecute = false })
                ?? throw new InvalidOperationException("macOS could not open the downloaded update package.");
        }

        if (update.FilePath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            var current = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(current) && current.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            {
                var helper = Path.Combine(Path.GetDirectoryName(update.FilePath)!, "apply-update.sh");
                var pid = Environment.ProcessId;
                var script = $"#!/bin/sh\nset -eu\nwhile kill -0 {pid} 2>/dev/null; do sleep 1; done\ncp '{EscapeShell(update.FilePath)}' '{EscapeShell(current)}.new'\nchmod +x '{EscapeShell(current)}.new'\nmv -f '{EscapeShell(current)}.new' '{EscapeShell(current)}'\nexec '{EscapeShell(current)}'\n";
                File.WriteAllText(helper, script);
                Process.Start("chmod", $"+x \"{helper}\"")?.WaitForExit();
                return Process.Start(new ProcessStartInfo("sh", $"\"{helper}\"") { UseShellExecute = false })
                    ?? throw new InvalidOperationException("Linux could not start the AppImage updater.");
            }
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = update.FilePath,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("The operating system could not open the downloaded update package.");
    }

    private static string EscapeShell(string value) => value.Replace("'", "'\\''");
    private static string EscapePowerShellSingleQuoted(string value) => value.Replace("'", "''");

    private static string[] DesiredAssetHints()
    {
        if (OperatingSystem.IsWindows()) return ["Windows-Setup.exe", "Setup.exe", "win-x64"];
        if (OperatingSystem.IsMacOS()) return RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? ["osx-arm64.dmg", "macOS-arm64", "osx-arm64"]
            : ["osx-x64.dmg", "macOS-x64", "osx-x64"];
        return RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? ["linux-arm64.AppImage", "linux-arm64"]
            : ["Linux-x64.AppImage", "linux-x64.AppImage", "Linux-x64"];
    }

    internal static int CompareVersions(string left, string right)
    {
        var l = ParseVersion(left);
        var r = ParseVersion(right);
        for (var i = 0; i < 3; i++)
        {
            var c = l.Core[i].CompareTo(r.Core[i]);
            if (c != 0) return c;
        }

        if (l.Pre.Length == 0 && r.Pre.Length == 0) return 0;
        if (l.Pre.Length == 0) return 1;
        if (r.Pre.Length == 0) return -1;
        var max = Math.Max(l.Pre.Length, r.Pre.Length);
        for (var i = 0; i < max; i++)
        {
            if (i >= l.Pre.Length) return -1;
            if (i >= r.Pre.Length) return 1;
            var ln = int.TryParse(l.Pre[i], out var li);
            var rn = int.TryParse(r.Pre[i], out var ri);
            var c = ln && rn ? li.CompareTo(ri) : string.Compare(l.Pre[i], r.Pre[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }

    internal static bool ShouldConsiderRelease(bool prerelease, string currentVersion) =>
        !prerelease || currentVersion.Contains('-', StringComparison.Ordinal);

    private static (int[] Core, string[] Pre) ParseVersion(string value)
    {
        var clean = value.Trim().TrimStart('v', 'V');
        var split = clean.Split('-', 2);
        var coreParts = split[0].Split('.');
        var core = new int[3];
        for (var i = 0; i < core.Length && i < coreParts.Length; i++)
            _ = int.TryParse(coreParts[i], out core[i]);
        var pre = split.Length > 1
            ? split[1].Replace('.', '-').Split('-', StringSplitOptions.RemoveEmptyEntries)
            : [];
        return (core, pre);
    }
}
