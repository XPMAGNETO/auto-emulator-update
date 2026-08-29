using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class FrontendImportService(PlatformService platform)
{
    public sealed record FrontendRoot(string Name, string Path);

    private static readonly HashSet<string> FrontendNames = new(new[]
    {
        "LaunchBox", "RetroBat", "ES-DE", "EmulationStation", "Pegasus", "pegasus-frontend",
        "RetroFE", "HyperSpin", "RocketLauncher", "GameEx", "mGalaxy", "CoinOPS", "Playnite",
        "steam-rom-manager", "Steam ROM Manager"
    }, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<FrontendRoot> DetectRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<FrontendRoot>();

        if (OperatingSystem.IsWindows())
        {
            var app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.AddRange([
                new("Playnite", Path.Combine(app, "Playnite")),
                new("Playnite", Path.Combine(local, "Playnite")),
                new("Pegasus", Path.Combine(app, "pegasus-frontend")),
                new("Nostlan", Path.Combine(app, "Nostlan")),
                new("Steam ROM Manager", Path.Combine(app, "steam-rom-manager"))
            ]);

            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady &&
                         (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
            {
                foreach (var root in FindFrontendDirectories(d.RootDirectory.FullName, FrontendNames, 5, 40000, true))
                    candidates.Add(root);
            }
        }
        else if (platform.IsBatocera)
        {
            candidates.Add(new("Batocera", "/userdata/system/configs"));
            candidates.Add(new("EmulationStation", "/userdata/system/configs/emulationstation"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
            candidates.AddRange([
                new("ES-DE", Path.Combine(home, "ES-DE")),
                new("ES-DE", Path.Combine(home, "Library", "Application Support", "ES-DE")),
                new("EmulationStation", Path.Combine(home, ".emulationstation")),
                new("Pegasus", Path.Combine(config, "pegasus-frontend")),
                new("Pegasus", Path.Combine(home, "Library", "Application Support", "pegasus-frontend")),
                new("Steam ROM Manager", Path.Combine(config, "steam-rom-manager")),
                new("Steam ROM Manager", Path.Combine(home, "Library", "Application Support", "steam-rom-manager"))
            ]);

            foreach (var seed in new[]
            {
                Path.Combine(home, "Applications"),
                Path.Combine(home, "Downloads"),
                Path.Combine(home, "Documents"),
                Path.Combine(home, "Games"),
                Path.Combine(home, "Library", "Application Support"),
                "/Applications",
                "/Volumes"
            }.Where(Directory.Exists))
            {
                foreach (var root in FindFrontendDirectories(seed, FrontendNames, 4, 25000, false))
                    candidates.Add(root);
            }
        }
        else
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
            candidates.AddRange([
                new("ES-DE", Path.Combine(home, "ES-DE")),
                new("ES-DE", Path.Combine(config, "ES-DE")),
                new("EmulationStation", Path.Combine(home, ".emulationstation")),
                new("Pegasus", Path.Combine(config, "pegasus-frontend")),
                new("Steam ROM Manager", Path.Combine(config, "steam-rom-manager")),
                new("Steam ROM Manager", Path.Combine(home, ".var", "app", "com.steamgriddb.steam-rom-manager"))
            ]);

            foreach (var seed in new[]
            {
                Path.Combine(home, "Applications"),
                Path.Combine(home, "Downloads"),
                Path.Combine(home, "Documents"),
                Path.Combine(home, "Games"),
                Path.Combine(home, ".config"),
                Path.Combine(home, ".var", "app"),
                "/opt",
                "/mnt",
                "/media",
                "/run/media"
            }.Where(Directory.Exists))
            {
                foreach (var root in FindFrontendDirectories(seed, FrontendNames, 4, 25000, false))
                    candidates.Add(root);
            }
        }

        return candidates.Where(x => Directory.Exists(x.Path))
            .DistinctBy(x => Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, List<string>>> ImportExecutablePathsAsync(
        IEnumerable<FrontendRoot> roots,
        IReadOnlyList<EmulatorDefinition> definitions,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var names = definitions.SelectMany(d => GetExes(d).Select(e => (d, e))).ToArray();

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (platform.IsBatocera && root.Name.Equals("Batocera", StringComparison.OrdinalIgnoreCase))
                continue;

            var launchboxXml = Path.Combine(root.Path, "Data", "Emulators.xml");
            if (File.Exists(launchboxXml))
            {
                try
                {
                    var doc = XDocument.Load(launchboxXml);
                    foreach (var node in doc.Descendants().Where(x => x.Name.LocalName.Contains("Emulator", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var value in node.Descendants().Select(x => x.Value).Where(v => v.Contains(".exe", StringComparison.OrdinalIgnoreCase)))
                            AddIfMatch(value, root, names, result);
                    }
                }
                catch { }
            }

            foreach (var file in EnumerateConfigs(root.Path, 6, 1500))
            {
                string text;
                try { text = await File.ReadAllTextAsync(file, ct); } catch { continue; }
                foreach (var (_, exe) in names)
                {
                    if (!text.Contains(exe, StringComparison.OrdinalIgnoreCase)) continue;
                    var rx = new Regex($@"(?:""|')?(?<p>(?:[A-Za-z]:\\|/|\.\.?[/\\])[^""'\r\n]*?{Regex.Escape(exe)})(?:""|')?", RegexOptions.IgnoreCase);
                    foreach (Match m in rx.Matches(text))
                        AddIfMatch(m.Groups["p"].Value, root, names, result, Path.GetDirectoryName(file));
                }
            }
        }
        return result;
    }

    private static IEnumerable<FrontendRoot> FindFrontendDirectories(
        string root,
        HashSet<string> names,
        int maxDepth,
        int maxVisited,
        bool useWindowsSkips)
    {
        var q = new Queue<(string path, int depth)>();
        q.Enqueue((root, 0));
        var visited = 0;
        while (q.Count > 0 && visited < maxVisited)
        {
            var (dir, depth) = q.Dequeue();
            visited++;
            string[] children;
            try { children = Directory.GetDirectories(dir); } catch { continue; }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (names.Contains(name))
                {
                    var display = name.Equals("pegasus-frontend", StringComparison.OrdinalIgnoreCase) ? "Pegasus" :
                                  name.Equals("steam-rom-manager", StringComparison.OrdinalIgnoreCase) ? "Steam ROM Manager" : name;
                    yield return new FrontendRoot(display, child);
                }

                var skip = useWindowsSkips
                    ? PlatformService.IsSkippedWindowsDirectory(name)
                    : name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                      name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                      name.Equals("Caches", StringComparison.OrdinalIgnoreCase) ||
                      name.Equals("cache", StringComparison.OrdinalIgnoreCase);
                if (depth < maxDepth && !skip)
                    q.Enqueue((child, depth + 1));
            }
        }
    }

    private static IEnumerable<string> EnumerateConfigs(string root, int maxDepth, int maxFiles)
    {
        var results = new List<string>();
        var q = new Queue<(string path, int depth)>();
        q.Enqueue((root, 0));
        var exts = new HashSet<string>(new[] { ".xml", ".json", ".cfg", ".ini", ".yaml", ".yml", ".txt", ".conf", ".config" }, StringComparer.OrdinalIgnoreCase);

        while (q.Count > 0 && results.Count < maxFiles)
        {
            var (dir, depth) = q.Dequeue();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(dir);
                directories = depth < maxDepth ? Directory.GetDirectories(dir) : [];
            }
            catch { continue; }

            foreach (var file in files)
            {
                if (results.Count >= maxFiles) break;
                try
                {
                    if (exts.Contains(Path.GetExtension(file)) && new FileInfo(file).Length < 5 * 1024 * 1024)
                        results.Add(file);
                }
                catch { }
            }
            foreach (var child in directories)
                q.Enqueue((child, depth + 1));
        }
        return results;
    }

    private string[] GetExes(EmulatorDefinition d)
        => d.Executables.TryGetValue(platform.Os, out var e) ? e :
           d.Executables.TryGetValue("any", out var a) ? a : [];

    private static void AddIfMatch(
        string raw,
        FrontendRoot root,
        (EmulatorDefinition d, string exe)[] names,
        Dictionary<string, List<string>> result,
        string? configDir = null)
    {
        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"', '\''));
        var candidates = new List<string> { expanded };
        if (!Path.IsPathRooted(expanded))
        {
            if (configDir is not null) candidates.Add(Path.GetFullPath(Path.Combine(configDir, expanded)));
            candidates.Add(Path.GetFullPath(Path.Combine(root.Path, expanded)));
        }
        foreach (var p in candidates.Where(File.Exists))
        {
            var file = Path.GetFileName(p);
            foreach (var (def, exe) in names.Where(x => file.Equals(x.exe, StringComparison.OrdinalIgnoreCase)))
            {
                if (!result.TryGetValue(def.Id, out var list)) result[def.Id] = list = [];
                if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
            }
        }
    }
}
