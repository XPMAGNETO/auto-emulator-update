using System.Runtime.InteropServices;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class PlatformService
{
    public string Os =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";

    public string Arch => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
    };

    public string RuntimeId => $"{Os}-{Arch}";

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
            yield return "/opt";
        }
    }

    public bool IsExecutablePath(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
