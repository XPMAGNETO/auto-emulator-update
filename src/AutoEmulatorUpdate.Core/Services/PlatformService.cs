using System.Runtime.InteropServices;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class PlatformService
{
    private readonly Lazy<string> _linuxDistribution = new(DetectLinuxDistribution);
    private readonly Lazy<bool> _hasRetroBat = new(DetectRetroBat);

    public string LinuxDistribution => OperatingSystem.IsLinux() ? _linuxDistribution.Value : "";
    public bool IsSteamOS => LinuxDistribution == "steamos";
    public bool IsPopOS => LinuxDistribution == "pop";
    public bool IsCachyOS => LinuxDistribution == "cachyos";
    public bool IsBatocera => LinuxDistribution == "batocera";
    public bool HasRetroBat => OperatingSystem.IsWindows() && _hasRetroBat.Value;

    public string Os =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";

    public string PlatformName =>
        HasRetroBat ? "Windows + RetroBat" :
        IsSteamOS ? "SteamOS" :
        IsPopOS ? "Pop!_OS" :
        IsCachyOS ? "CachyOS" :
        IsBatocera ? "Batocera" :
        Os switch
        {
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
            _ => "Unknown"
        };

    public string Arch => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
    };

    public string RuntimeId => LinuxDistribution switch
    {
        "steamos" => $"steamos-{Arch}",
        "pop" => $"popos-{Arch}",
        "cachyos" => $"cachyos-{Arch}",
        "batocera" => $"batocera-{Arch}",
        _ when HasRetroBat => $"retrobat-windows-{Arch}",
        _ => $"{Os}-{Arch}"
    };

    public bool SystemOwnsBuiltInEmulators => IsBatocera;

    public IEnumerable<string> DefaultSearchRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady &&
                         (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
                yield return drive.RootDirectory.FullName;

            foreach (var path in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.Combine(home, "Downloads"),
                Path.Combine(home, "Documents"),
                Path.Combine(home, "Emulators"),
                Path.Combine(home, "Games")
            }.Where(p => !string.IsNullOrWhiteSpace(p)))
                yield return path;
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications";
            yield return Path.Combine(home, "Applications");
            yield return Path.Combine(home, "Emulators");
        }
        else if (IsBatocera)
        {
            yield return "/userdata/system/auto-emulator-update/emulators";
            yield return "/userdata/system/emulators";
            yield return "/userdata/system/apps";
            yield return "/userdata/system/configs";
            yield return "/userdata/saves/flatpak";
        }
        else
        {
            yield return Path.Combine(home, "Applications");
            yield return Path.Combine(home, "Emulators");
            yield return Path.Combine(home, ".local", "bin");
            yield return Path.Combine(home, ".local", "share", "flatpak", "exports", "bin");
            yield return "/var/lib/flatpak/exports/bin";

            if (IsSteamOS)
            {
                yield return Path.Combine(home, ".var", "app");
                yield return "/run/media";
                yield return "/run/media/deck";
            }
            else
            {
                yield return "/opt";
            }
        }
    }

    public bool IsExecutablePath(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static string IdentifyLinuxDistribution(IEnumerable<string> osReleaseLines)
    {
        try
        {
            var values = osReleaseLines
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0].Trim(),
                    parts => parts[1].Trim().Trim('"'),
                    StringComparer.OrdinalIgnoreCase);

            values.TryGetValue("ID", out var id);
            values.TryGetValue("NAME", out var name);
            values.TryGetValue("VARIANT_ID", out var variant);
            values.TryGetValue("ID_LIKE", out var idLike);

            var all = string.Join(' ', new[] { id, name, variant, idLike }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

            if (string.Equals(id, "steamos", StringComparison.OrdinalIgnoreCase) || all.Contains("steamos"))
                return "steamos";
            if (string.Equals(id, "pop", StringComparison.OrdinalIgnoreCase) || all.Contains("pop!_os") || all.Contains("pop os"))
                return "pop";
            if (string.Equals(id, "cachyos", StringComparison.OrdinalIgnoreCase) || all.Contains("cachyos"))
                return "cachyos";
            if (string.Equals(id, "batocera", StringComparison.OrdinalIgnoreCase) || all.Contains("batocera"))
                return "batocera";

            return string.IsNullOrWhiteSpace(id) ? "linux" : id.ToLowerInvariant();
        }
        catch
        {
            return "linux";
        }
    }

    private static string DetectLinuxDistribution()
    {
        try
        {
            const string osRelease = "/etc/os-release";
            if (File.Exists(osRelease))
            {
                var distro = IdentifyLinuxDistribution(File.ReadLines(osRelease));
                if (distro != "linux") return distro;
            }

            if (File.Exists("/usr/bin/batocera-check-updates") || File.Exists("/usr/bin/batocera-upgrade"))
                return "batocera";
        }
        catch { }

        return "linux";
    }

    private static bool DetectRetroBat()
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady &&
                         (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
            {
                var queue = new Queue<(string path, int depth)>();
                queue.Enqueue((drive.RootDirectory.FullName, 0));
                var visited = 0;
                while (queue.Count > 0 && visited < 30000)
                {
                    var (dir, depth) = queue.Dequeue();
                    visited++;
                    string[] children;
                    try { children = Directory.GetDirectories(dir); } catch { continue; }
                    foreach (var child in children)
                    {
                        var name = Path.GetFileName(child);
                        if (name.Equals("RetroBat", StringComparison.OrdinalIgnoreCase) &&
                            (File.Exists(Path.Combine(child, "retrobat.exe")) ||
                             File.Exists(Path.Combine(child, "RetroBat.exe")) ||
                             Directory.Exists(Path.Combine(child, "emulators"))))
                            return true;
                        if (depth < 5 && !IsSkippedWindowsDirectory(name))
                            queue.Enqueue((child, depth + 1));
                    }
                }
            }
        }
        catch { }
        return false;
    }

    internal static bool IsSkippedWindowsDirectory(string name) =>
        name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Recovery", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
        name.Equals(".git", StringComparison.OrdinalIgnoreCase);
}
