using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class ReleaseResolverService(HttpClient http, PlatformService platform)
{
    public async Task<ReleaseInfo> ResolveAsync(EmulatorDefinition def, UpdateChannel channel, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var source in def.Sources)
        {
            if (!string.IsNullOrWhiteSpace(source.Channel) &&
                !source.Channel.Equals(channel.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                return source.Kind.ToLowerInvariant() switch
                {
                    "github" => await ResolveGitHubAsync(def, source, channel, ct),
                    "github-tags" => await ResolveGitHubTagsAsync(def, source, channel, ct),
                    "html-regex" => await ResolveHtmlRegexAsync(def, source, channel, ct),
                    "static" => ResolveStatic(def, source, channel),
                    _ => throw new NotSupportedException($"Resolver '{source.Kind}' is not supported.")
                };
            }
            catch (Exception ex) { errors.Add($"{source.Kind}: {ex.Message}"); }
        }
        throw new InvalidOperationException($"All update sources failed for {def.Name}: {string.Join(" | ", errors)}");
    }

    private async Task<ReleaseInfo> ResolveGitHubAsync(EmulatorDefinition def, ReleaseSource source, UpdateChannel channel, CancellationToken ct)
    {
        var repo = source.Repository ?? throw new InvalidDataException("GitHub repository missing.");
        var url = channel == UpdateChannel.Stable
            ? $"https://api.github.com/repos/{repo}/releases/latest"
            : $"https://api.github.com/repos/{repo}/releases?per_page=20";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("AutoEmulatorUpdate/10");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var release = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().FirstOrDefault(r => channel != UpdateChannel.Stable || !r.GetProperty("prerelease").GetBoolean())
            : doc.RootElement;

        if (release.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("No suitable release.");

        var version = release.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("tag_name missing.");
        var pkg = PackageFor(def);
        JsonElement? selected = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (GlobMatch(name, pkg.AssetPattern)) { selected = asset; break; }
        }
        if (selected is null) throw new InvalidDataException($"No asset matched '{pkg.AssetPattern}' for {platform.RuntimeId}.");

        var a = selected.Value;
        var assetName = a.GetProperty("name").GetString()!;
        var dl = a.GetProperty("browser_download_url").GetString()!;
        long? size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : null;
        string? sha = null;
        if (a.TryGetProperty("digest", out var dig))
        {
            var d = dig.GetString();
            if (d?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) sha = d[7..];
        }
        return new ReleaseInfo(
            version, dl, assetName, $"GitHub:{repo}", size, sha,
            release.TryGetProperty("body", out var body) ? body.GetString() : null,
            release.TryGetProperty("html_url", out var html) ? html.GetString() : null,
            channel);
    }

    private async Task<ReleaseInfo> ResolveGitHubTagsAsync(EmulatorDefinition def, ReleaseSource source, UpdateChannel channel, CancellationToken ct)
    {
        var repo = source.Repository ?? throw new InvalidDataException("Repository missing.");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/tags?per_page=30");
        req.Headers.UserAgent.ParseAdd("AutoEmulatorUpdate/10");
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var tag = doc.RootElement.EnumerateArray().Select(x => x.GetProperty("name").GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                  ?? throw new InvalidDataException("No tags found.");
        var version = tag.TrimStart('v','V');
        var pkg = PackageFor(def);
        if (string.IsNullOrWhiteSpace(pkg.DirectUrlTemplate))
            throw new InvalidDataException("github-tags requires DirectUrlTemplate.");
        var dl = pkg.DirectUrlTemplate.Replace("{tag}", tag).Replace("{version}", version);
        return new ReleaseInfo(tag, dl, Path.GetFileName(new Uri(dl).AbsolutePath), $"GitHubTags:{repo}", Channel: channel);
    }

    private async Task<ReleaseInfo> ResolveHtmlRegexAsync(EmulatorDefinition def, ReleaseSource source, UpdateChannel channel, CancellationToken ct)
    {
        var url = source.Url ?? throw new InvalidDataException("URL missing.");
        var regex = source.VersionRegex ?? throw new InvalidDataException("VersionRegex missing.");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 AutoEmulatorUpdate/10");
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync(ct);
        var m = Regex.Match(html, regex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) throw new InvalidDataException("Version regex did not match.");
        var version = m.Groups["version"].Success ? m.Groups["version"].Value : m.Groups[1].Value;
        var pkg = PackageFor(def);
        if (string.IsNullOrWhiteSpace(pkg.DirectUrlTemplate)) throw new InvalidDataException("DirectUrlTemplate missing.");
        var dl = pkg.DirectUrlTemplate.Replace("{version}", version).Replace("{tag}", version);
        return new ReleaseInfo(version, dl, Path.GetFileName(new Uri(dl).AbsolutePath), url, Channel: channel);
    }

    private ReleaseInfo ResolveStatic(EmulatorDefinition def, ReleaseSource source, UpdateChannel channel)
    {
        var version = source.VersionRegex ?? throw new InvalidDataException("Static version missing.");
        var pkg = PackageFor(def);
        var dl = pkg.DirectUrlTemplate?.Replace("{version}", version).Replace("{tag}", version)
                 ?? throw new InvalidDataException("Static DirectUrlTemplate missing.");
        return new ReleaseInfo(version, dl, Path.GetFileName(new Uri(dl).AbsolutePath), "Static", Channel: channel);
    }

    private PlatformPackage PackageFor(EmulatorDefinition def) =>
        def.Packages.FirstOrDefault(p =>
            p.Os.Equals(platform.Os, StringComparison.OrdinalIgnoreCase) &&
            (p.Arch.Equals(platform.Arch, StringComparison.OrdinalIgnoreCase) || p.Arch.Equals("any", StringComparison.OrdinalIgnoreCase)))
        ?? throw new PlatformNotSupportedException($"{def.Name} has no {platform.RuntimeId} package definition.");

    private static bool GlobMatch(string text, string glob)
    {
        var rx = "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(text, rx, RegexOptions.IgnoreCase);
    }
}
