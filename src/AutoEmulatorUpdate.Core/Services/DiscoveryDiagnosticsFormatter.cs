using System.Text;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public static class DiscoveryDiagnosticsFormatter
{
    private static readonly string[] KnownFrontends =
    [
        "LaunchBox", "RetroBat", "ES-DE", "EmulationStation", "Pegasus", "RetroFE",
        "HyperSpin", "RocketLauncher", "GameEx", "mGalaxy", "CoinOPS", "Playnite",
        "Steam ROM Manager", "Batocera"
    ];

    public static string Build(
        DiscoveryScanDiagnostics diagnostics,
        IReadOnlyList<EmulatorDefinition> definitions,
        IEnumerable<(string Name, string Path)> frontendRoots,
        IEnumerable<InstalledEmulator>? installed = null)
    {
        var sb = new StringBuilder();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string S(string value) => SanitizePath(value, home);

        var installedIds = (installed ?? [])
            .Select(x => x.Definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("Auto Emulator Update — Discovery diagnostics");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Scan started: {diagnostics.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Platform: {diagnostics.Platform}");
        sb.AppendLine($"Runtime: {diagnostics.RuntimeId}");
        sb.AppendLine();

        sb.AppendLine("SEARCH ROOTS");
        foreach (var root in diagnostics.RequestedRoots)
        {
            var state = diagnostics.MissingRoots.Contains(root, StringComparer.OrdinalIgnoreCase) ? "missing" : "considered";
            sb.AppendLine($"- {S(root)} [{state}]");
        }
        if (diagnostics.RequestedRoots.Count == 0) sb.AppendLine("- No roots were configured.");
        sb.AppendLine();

        sb.AppendLine("SCAN SUMMARY");
        sb.AppendLine($"- Directories scanned: {diagnostics.ScannedDirectories.Count}");
        sb.AppendLine($"- Skipped/excluded: {diagnostics.SkippedDirectories.Count}");
        sb.AppendLine($"- Access/path failures: {diagnostics.AccessFailures.Count}");
        sb.AppendLine($"- Duplicate paths suppressed: {diagnostics.DuplicateDirectories.Count}");
        sb.AppendLine($"- Emulator definitions detected: {diagnostics.DetectedDefinitions.Count}");
        sb.AppendLine();

        AppendEntries(sb, "SKIPPED / EXCLUDED", diagnostics.SkippedDirectories.Select(x => (S(x.Path), x.Reason)), 200);
        AppendEntries(sb, "ACCESS / PATH FAILURES", diagnostics.AccessFailures.Select(x => (S(x.Path), x.Reason)), 200);
        AppendEntries(sb, "DUPLICATE PATHS SUPPRESSED", diagnostics.DuplicateDirectories.Select(x => (S(x), "Already queued/scanned")), 200);

        sb.AppendLine("FRONTEND DETECTION ATTEMPTS");
        sb.AppendLine($"- Frontend names considered: {string.Join(", ", KnownFrontends)}");
        var roots = frontendRoots.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0)
            sb.AppendLine("- No supported frontend root was found in the platform-specific candidate and recursive search locations.");
        else
            foreach (var root in roots) sb.AppendLine($"- {root.Name}: {S(root.Path)} [detected]");
        sb.AppendLine();

        sb.AppendLine("MANIFEST DEFINITIONS / WHY WASN'T THIS FOUND?");
        foreach (var def in definitions.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.ExpectedExecutables.TryGetValue(def.Id, out var expected);
            expected ??= [];
            var detected = diagnostics.DetectedDefinitions.Contains(def.Id, StringComparer.OrdinalIgnoreCase) || installedIds.Contains(def.Id);
            var reason = detected
                ? "Detected."
                : expected.Length == 0
                    ? $"Not detected: this manifest has no executable/package name for {diagnostics.RuntimeId}."
                    : diagnostics.ScannedDirectories.Count == 0
                        ? "Not detected: no searchable directory was scanned."
                        : "Not detected: none of the expected executable/package names matched within the scanned roots; review missing roots, skipped paths, access failures, custom install paths, and frontend configuration below.";

            sb.AppendLine($"- {def.Name} ({def.Id})");
            sb.AppendLine($"  Expected: {(expected.Length == 0 ? "<none for this platform>" : string.Join(", ", expected))}");
            sb.AppendLine($"  Result: {reason}");
        }
        sb.AppendLine();

        sb.AppendLine("PRIVACY");
        sb.AppendLine("- The user-profile path is replaced with '~'. This report does not include file contents, credentials, environment-variable values, ROM names, or update tokens.");
        sb.AppendLine("- Review the report before sharing because custom folder names can still be identifying.");
        return sb.ToString();
    }

    public static string SanitizePath(string path, string? home = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var value = path;
        if (!string.IsNullOrWhiteSpace(home) && value.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            value = "~" + value[home.Length..];
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(temp) && value.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            value = "<temp>" + value[temp.Length..];
        return value;
    }

    private static void AppendEntries(StringBuilder sb, string title, IEnumerable<(string Path, string Reason)> entries, int limit)
    {
        sb.AppendLine(title);
        var list = entries.Take(limit + 1).ToArray();
        if (list.Length == 0) sb.AppendLine("- None recorded.");
        else
        {
            foreach (var item in list.Take(limit)) sb.AppendLine($"- {item.Path} — {item.Reason}");
            if (list.Length > limit) sb.AppendLine($"- … additional entries omitted after {limit} items.");
        }
        sb.AppendLine();
    }
}
