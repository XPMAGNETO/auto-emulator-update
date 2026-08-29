using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class InstallService(
    ReleaseResolverService resolver,
    DownloadService downloads,
    ArchiveService archives,
    PlatformService platform)
{
    public string DefaultRoot(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DefaultInstallRoot))
            return settings.DefaultInstallRoot;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Emulators");
        }
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Applications", "Emulators");
        if (platform.IsBatocera)
            return "/userdata/system/auto-emulator-update/emulators";
        return Path.Combine(home, "Emulators");
    }

    public async Task<InstalledEmulator> InstallAsync(
        EmulatorDefinition definition,
        AppSettings settings,
        IProgress<(double percent, string message)>? progress = null,
        CancellationToken ct = default)
    {
        var release = await resolver.ResolveAsync(definition, UpdateChannel.Stable, ct);
        var target = Path.Combine(DefaultRoot(settings), Sanitize(definition.Name));
        Directory.CreateDirectory(target);

        var package = await downloads.DownloadAsync(
            definition.Id,
            release,
            settings.BandwidthLimitMBps,
            new Progress<double>(p => progress?.Report((p * .65, $"Downloading {definition.Name}"))),
            ct);

        var staging = Path.Combine(Path.GetTempPath(), "AEU-Install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await archives.ExtractAsync(package, staging,
                new Progress<string>(m => progress?.Report((70, m))), ct);

            var source = CollapseSingleDirectory(staging);
            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
            for (var i = 0; i < files.Length; i++)
            {
                var dest = Path.Combine(target, Path.GetRelativePath(source, files[i]));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(files[i], dest, true);
                progress?.Report((70 + (i + 1) * 30d / Math.Max(1, files.Length), "Installing"));
                await Task.Yield();
            }
        }
        finally { try { Directory.Delete(staging, true); } catch { } }

        var exeNames = definition.Executables.TryGetValue(platform.Os, out var osExes)
            ? osExes
            : definition.Executables.TryGetValue("any", out var any) ? any : [];

        var exe = exeNames.SelectMany(name => Directory.EnumerateFiles(target, name, SearchOption.AllDirectories))
            .FirstOrDefault();

        if (exe is null)
            throw new InvalidDataException("Installation finished, but the expected emulator executable was not found.");

        return new InstalledEmulator
        {
            Definition = definition,
            InstallPath = Path.GetDirectoryName(exe)!,
            ExecutablePath = exe,
            CurrentVersion = release.Version,
            LatestVersion = release.Version,
            DetectionMethod = platform.IsBatocera
                ? "Installed in Batocera userdata by Auto Emulator Update"
                : "Installed by Auto Emulator Update",
            Confidence = "High",
            FrontendOwner = platform.IsBatocera ? "Standalone on Batocera" : "Auto Emulator Update",
            Status = "Up to date",
            SourceHealth = SourceHealthState.Healthy,
            SourceName = release.Source
        };
    }

    private static string CollapseSingleDirectory(string dir)
    {
        var files = Directory.EnumerateFiles(dir).Any();
        var dirs = Directory.EnumerateDirectories(dir).ToArray();
        return !files && dirs.Length == 1 ? dirs[0] : dir;
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}
