using System.Diagnostics;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record DiscoveryDiagnosticEntry(string Path, string Reason);

public sealed record DiscoveryScanDiagnostics(
    DateTimeOffset StartedAt,
    string Platform,
    string RuntimeId,
    IReadOnlyList<string> RequestedRoots,
    IReadOnlyList<string> MissingRoots,
    IReadOnlyList<string> ScannedDirectories,
    IReadOnlyList<DiscoveryDiagnosticEntry> SkippedDirectories,
    IReadOnlyList<DiscoveryDiagnosticEntry> AccessFailures,
    IReadOnlyList<string> DuplicateDirectories,
    IReadOnlyDictionary<string, string[]> ExpectedExecutables,
    IReadOnlyList<string> DetectedDefinitions)
{
    public static DiscoveryScanDiagnostics Empty(string platform, string runtimeId) => new(
        DateTimeOffset.UtcNow,
        platform,
        runtimeId,
        [], [], [], [], [], [],
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
        []);
}

public sealed class DiscoveryService(PlatformService platform, VersionService versions)
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "WinSxS", "System Volume Information", "$Recycle.Bin", "Recovery",
        ".git", "node_modules", ".cache", "Caches"
    };

    public DiscoveryScanDiagnostics LastDiagnostics { get; private set; } =
        DiscoveryScanDiagnostics.Empty(platform.PlatformName, platform.RuntimeId);

    public async Task<List<InstalledEmulator>> ScanAsync(
        IReadOnlyList<EmulatorDefinition> definitions,
        IEnumerable<string> roots,
        IProgress<(string message, double? percent)>? progress = null,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var requestedRoots = roots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingRoots = new List<string>();
        var scannedDirectories = new List<string>();
        var skippedDirectories = new List<DiscoveryDiagnosticEntry>();
        var accessFailures = new List<DiscoveryDiagnosticEntry>();
        var duplicateDirectories = new List<string>();
        var detectedDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var exeMap = new Dictionary<string, List<EmulatorDefinition>>(StringComparer.OrdinalIgnoreCase);
        var expectedExecutables = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in definitions)
        {
            var exes = ExecutablesFor(d);
            expectedExecutables[d.Id] = exes;
            foreach (var exe in exes)
            {
                if (!exeMap.TryGetValue(exe, out var list)) exeMap[exe] = list = [];
                list.Add(d);
            }
        }

        var found = new Dictionary<string, InstalledEmulator>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string path, int depth)>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in requestedRoots)
        {
            if (!Directory.Exists(root))
            {
                missingRoots.Add(root);
                continue;
            }

            string full;
            try { full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex)
            {
                accessFailures.Add(new(root, $"Invalid path: {ex.GetType().Name}"));
                continue;
            }

            if (queued.Add(full)) queue.Enqueue((full, 0));
            else duplicateDirectories.Add(full);
        }

        int visited = 0;
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();
            visited++;
            scannedDirectories.Add(dir);
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
                        if (found.ContainsKey(key))
                        {
                            duplicateDirectories.Add(Path.GetDirectoryName(file)!);
                            continue;
                        }
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
                        detectedDefinitions.Add(def.Id);
                    }
                }

                if (depth < 5)
                {
                    foreach (var child in Directory.EnumerateDirectories(dir))
                    {
                        var name = Path.GetFileName(child);
                        if (Skip.Contains(name))
                        {
                            skippedDirectories.Add(new(child, "Excluded system/cache/problem directory"));
                            continue;
                        }
                        string full;
                        try { full = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                        catch (Exception ex)
                        {
                            accessFailures.Add(new(child, $"Invalid path: {ex.GetType().Name}"));
                            continue;
                        }
                        if (queued.Add(full)) queue.Enqueue((full, depth + 1));
                        else duplicateDirectories.Add(full);
                    }
                }
                else
                {
                    skippedDirectories.Add(new(dir, "Maximum scan depth reached"));
                }
            }
            catch (UnauthorizedAccessException)
            {
                accessFailures.Add(new(dir, "Access denied"));
            }
            catch (IOException ex)
            {
                accessFailures.Add(new(dir, $"I/O error: {ex.GetType().Name}"));
            }
        }

        LastDiagnostics = new DiscoveryScanDiagnostics(
            startedAt,
            platform.PlatformName,
            platform.RuntimeId,
            requestedRoots,
            missingRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            scannedDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            skippedDirectories.DistinctBy(x => $"{x.Path}|{x.Reason}", StringComparer.OrdinalIgnoreCase).ToArray(),
            accessFailures.DistinctBy(x => $"{x.Path}|{x.Reason}", StringComparer.OrdinalIgnoreCase).ToArray(),
            duplicateDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            expectedExecutables,
            detectedDefinitions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());

        return found.Values.ToList();
    }

    public string[] ExecutablesFor(EmulatorDefinition def)
    {
        if (def.Executables.TryGetValue(platform.Os, out var os)) return os;
        if (def.Executables.TryGetValue("any", out var any)) return any;
        return [];
    }

    public async Task<(string? version, string method, string confidence)> DetectVersionAsync(string exe, CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                var v = versions.Extract(info.ProductVersion)
                        ?? versions.Extract(info.FileVersion)
                        ?? versions.Extract(info.Comments)
                        ?? versions.Extract(info.FileDescription);
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
                var stdoutTask = p.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderrTask = p.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await p.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    try { p.Kill(true); } catch { }
                    try { await p.WaitForExitAsync(CancellationToken.None); } catch { }
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(true); } catch { }
                    throw;
                }
                var stdout = await SafeOutputAsync(stdoutTask);
                var stderr = await SafeOutputAsync(stderrTask);
                var v = versions.Extract(stdout + "\n" + stderr);
                if (v is not null) return (v, "Command line", "High");
            }
            catch { }
        }

        var folder = Path.GetDirectoryName(exe);
        if (folder is not null)
        {
            foreach (var name in new[] { "version.txt", "VERSION", "version", "build-version.txt", "build.txt" })
            {
                try
                {
                    var path = Path.Combine(folder, name);
                    if (!File.Exists(path)) continue;
                    var text = await File.ReadAllTextAsync(path, ct);
                    var v = versions.Extract(text[..Math.Min(text.Length, 4096)]);
                    if (v is not null) return (v, $"Version file ({name})", "High");
                }
                catch { }
            }
        }

        var pathVersion = versions.Extract(Path.GetFileNameWithoutExtension(exe));
        if (pathVersion is not null) return (pathVersion, "Executable filename", "Medium");

        var parent = folder;
        for (var depth = 0; depth < 3 && !string.IsNullOrWhiteSpace(parent); depth++)
        {
            var v = versions.Extract(Path.GetFileName(parent));
            if (v is not null) return (v, "Install folder name", "Medium");
            parent = Path.GetDirectoryName(parent);
        }

        return (null, "Executable found", "Low");
    }

    private static async Task<string> SafeOutputAsync(Task<string> task)
    {
        try { return await task; }
        catch { return string.Empty; }
    }

    private static bool DetectPortable(string folder) =>
        new[] { "portable.txt", "portable.ini", "portable", "user" }.Any(x =>
            File.Exists(Path.Combine(folder, x)) || Directory.Exists(Path.Combine(folder, x)));
}
