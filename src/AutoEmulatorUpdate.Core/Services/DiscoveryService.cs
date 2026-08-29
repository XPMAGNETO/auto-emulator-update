using System.Diagnostics;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class DiscoveryService(PlatformService platform, VersionService versions)
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "WinSxS", "System Volume Information", "$Recycle.Bin", "Recovery",
        ".git", "node_modules", ".cache", "Caches"
    };

    public async Task<List<InstalledEmulator>> ScanAsync(
        IReadOnlyList<EmulatorDefinition> definitions,
        IEnumerable<string> roots,
        IProgress<(string message, double? percent)>? progress = null,
        CancellationToken ct = default)
    {
        var exeMap = new Dictionary<string, List<EmulatorDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in definitions)
        {
            foreach (var exe in ExecutablesFor(d))
            {
                if (!exeMap.TryGetValue(exe, out var list)) exeMap[exe] = list = [];
                list.Add(d);
            }
        }

        var found = new Dictionary<string, InstalledEmulator>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string path, int depth)>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            string full;
            try { full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { continue; }
            if (queued.Add(full)) queue.Enqueue((full, 0));
        }

        int visited = 0;
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();
            visited++;
            if (visited % 50 == 0)
            {
                progress?.Report(($"Scanning {dir}", null));
                await Task.Yield();
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    if (!exeMap.TryGetValue(name, out var defs)) continue;
                    foreach (var def in defs)
                    {
                        var key = $"{def.Id}|{Path.GetDirectoryName(file)}";
                        if (found.ContainsKey(key)) continue;
                        var (version, method, confidence) = await DetectVersionAsync(file, ct);
                        found[key] = new InstalledEmulator
                        {
                            Definition = def,
                            InstallPath = Path.GetDirectoryName(file)!,
                            ExecutablePath = file,
                            CurrentVersion = version ?? "Unknown",
                            DetectionMethod = method,
                            Confidence = confidence,
                            IsPortable = DetectPortable(Path.GetDirectoryName(file)!)
                        };
                    }
                }

                if (depth < 5)
                {
                    foreach (var child in Directory.EnumerateDirectories(dir))
                    {
                        var name = Path.GetFileName(child);
                        if (Skip.Contains(name)) continue;
                        string full;
                        try { full = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                        catch { continue; }
                        if (queued.Add(full)) queue.Enqueue((full, depth + 1));
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return found.Values.ToList();
    }

    public string[] ExecutablesFor(EmulatorDefinition def)
    {
        if (def.Executables.TryGetValue(platform.Os, out var os)) return os;
        if (def.Executables.TryGetValue("any", out var any)) return any;
        return [];
    }

    private async Task<(string? version, string method, string confidence)> DetectVersionAsync(string exe, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                var v = versions.Extract(info.ProductVersion) ?? versions.Extract(info.FileVersion);
                if (v is not null) return (v, "Executable metadata", "High");
            }
            catch { }
        }

        foreach (var arg in new[] { "--version", "-version", "-v" })
        {
            try
            {
                var psi = new ProcessStartInfo(exe, arg)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var stdout = await p.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = await p.StandardError.ReadToEndAsync(timeout.Token);
                try { await p.WaitForExitAsync(timeout.Token); } catch { try { p.Kill(true); } catch { } }
                var v = versions.Extract(stdout + "\n" + stderr);
                if (v is not null) return (v, "Command line", "High");
            }
            catch { }
        }

        return (null, "Executable found", "Low");
    }

    private static bool DetectPortable(string folder) =>
        new[] { "portable.txt", "portable.ini", "portable", "user" }.Any(x =>
            File.Exists(Path.Combine(folder, x)) || Directory.Exists(Path.Combine(folder, x)));
}
