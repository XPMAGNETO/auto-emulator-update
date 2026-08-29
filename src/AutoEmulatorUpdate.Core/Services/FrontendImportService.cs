using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class FrontendImportService(PlatformService platform)
{
    public sealed record FrontendRoot(string Name, string Path);

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

            // RetroBat is portable and can live on SSD/HDD/USB storage, so scan
            // both fixed and removable drives instead of assuming C:\RetroBat.
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady &&
                         (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
            {
                foreach (var name in new[] { "LaunchBox", "RetroBat", "ES-DE", "Pegasus", "RetroFE", "HyperSpin", "RocketLauncher", "GameEx", "mGalaxy", "CoinOPS" })
                    candidates.Add(new(name, Path.Combine(d.RootDirectory.FullName, name)));
            }
        }
        else if (platform.IsBatocera)
        {
            // Batocera itself owns the bundled emulator stack. We surface its
            // configuration root for visibility, but Auto Emulator Update only
            // discovers/manages standalone emulators under writable /userdata
            // paths from PlatformService.DefaultSearchRoots().
            candidates.Add(new("Batocera", "/userdata/system/configs"));
        }
        else
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
            candidates.AddRange([
                new("ES-DE", Path.Combine(home, "ES-DE")),
                new("EmulationStation", Path.Combine(home, ".emulationstation")),
                new("Pegasus", Path.Combine(config, "pegasus-frontend")),
                new("Steam ROM Manager", Path.Combine(config, "steam-rom-manager"))
            ]);
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

            // Do not import Batocera's built-in emulator executables for direct
            // replacement: Batocera updates those through its own updater.
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

            foreach (var file in EnumerateConfigs(root.Path, 5, 750))
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
            catch
            {
                continue;
            }

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
