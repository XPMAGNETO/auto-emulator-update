using System.Runtime.InteropServices;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class PlatformService
{
    private readonly Lazy<bool> _isSteamOs = new(DetectSteamOs);

    public bool IsSteamOS => OperatingSystem.IsLinux() && _isSteamOs.Value;

    // SteamOS uses Linux emulator packages/manifests, while still being exposed
    // as a distinct platform to the UI and platform-specific behavior.
    public string Os =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";

    public string PlatformName => IsSteamOS ? "SteamOS" : Os switch
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

    public string RuntimeId => IsSteamOS ? $"steamos-{Arch}" : $"{Os}-{Arch}";

    public IEnumerable<string> DefaultSearchRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                foreach (var sub in new[] { "Emulators", "Emulator", "Games", @"LaunchBox\Emulators", "RetroBat", "ES-DE" })
                    yield return Path.Combine(drive.RootDirectory.FullName, sub);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications";
            yield return Path.Combine(home, "Applications");
            yield return Path.Combine(home, "Emulators");
        }
        else
        {
            yield return Path.Combine(home, "Applications");
            yield return Path.Combine(home, "Emulators");
            yield return Path.Combine(home, ".local", "bin");

            if (IsSteamOS)
            {
                // SteamOS keeps user-installed software on writable storage.
                // Prefer user/AppImage/Flatpak locations and removable storage;
                // never require disabling the SteamOS read-only system image.
                yield return Path.Combine(home, ".local", "share", "flatpak", "exports", "bin");
                yield return "/var/lib/flatpak/exports/bin";
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

    private static bool DetectSteamOs()
    {
        try
        {
            const string osRelease = "/etc/os-release";
            if (!File.Exists(osRelease)) return false;

            var values = File.ReadLines(osRelease)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0].Trim(),
                    parts => parts[1].Trim().Trim('"'),
                    StringComparer.OrdinalIgnoreCase);

            if (values.TryGetValue("ID", out var id) &&
                id.Equals("steamos", StringComparison.OrdinalIgnoreCase))
                return true;

            if (values.TryGetValue("NAME", out var name) &&
                name.Contains("SteamOS", StringComparison.OrdinalIgnoreCase))
                return true;

            return values.TryGetValue("VARIANT_ID", out var variant) &&
                   variant.Contains("steam", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
