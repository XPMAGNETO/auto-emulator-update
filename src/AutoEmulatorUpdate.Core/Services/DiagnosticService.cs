using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AutoEmulatorUpdate.Core.Models;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class DiagnosticService(AppPaths paths)
{
    public async Task<string> CreateBundleAsync(
        AppSettings settings,
        IEnumerable<InstalledEmulator> installed,
        string? extraMessage = null,
        CancellationToken ct = default)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var temp = Path.Combine(Path.GetTempPath(), "AutoEmulatorUpdate-Diagnostic-" + stamp);
        Directory.CreateDirectory(temp);

        var system = new
        {
            AppVersion = AutoEmulatorUpdate.Core.BuildInfo.Version,
            OS = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Timestamp = DateTimeOffset.Now,
            ExtraMessage = extraMessage
        };
        await File.WriteAllTextAsync(
            Path.Combine(temp, "system.json"),
            JsonSerializer.Serialize(system, new JsonSerializerOptions { WriteIndented = true }), ct);

        var safeSettings = new
        {
            settings.AutoScanDrives,
            settings.AutoCloseAfterRun,
            settings.ParallelChecks,
            settings.BackupRetentionDays,
            settings.BackupMaxGb,
            settings.DownloadCacheDays,
            settings.BandwidthLimitMBps,
            settings.VerifyChecksums,
            settings.VerifySignatures,
            settings.AutoRollbackOnValidationFailure,
            settings.NotificationsEnabled,
            settings.FrontendProtectionDefault,
            settings.MaintenanceMode,
            settings.StartupBehavior,
            settings.ExperienceMode,
            settings.FirstRunCompleted,
            settings.BackgroundMode,
            settings.AutoAppUpdates,
            settings.ScheduleEnabled,
            settings.ScheduleTime,
            settings.ScheduleDays
        };
        await File.WriteAllTextAsync(
            Path.Combine(temp, "settings-sanitized.json"),
            JsonSerializer.Serialize(safeSettings, new JsonSerializerOptions { WriteIndented = true }), ct);

        var emulators = installed.Select(e => new
        {
            Id = e.Definition.Id,
            e.Definition.Name,
            e.CurrentVersion,
            e.LatestVersion,
            e.Status,
            e.DetectionMethod,
            e.Confidence,
            e.FrontendOwner,
            e.IsPortable,
            e.Channel,
            e.SourceHealth,
            e.SourceName
            // InstallPath intentionally omitted from the public diagnostic summary.
        }).ToArray();

        await File.WriteAllTextAsync(
            Path.Combine(temp, "emulators.json"),
            JsonSerializer.Serialize(emulators, new JsonSerializerOptions { WriteIndented = true }), ct);

        foreach (var file in Directory.Exists(paths.LogsRoot)
                     ? Directory.EnumerateFiles(paths.LogsRoot, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3)
                     : [])
        {
            File.Copy(file, Path.Combine(temp, Path.GetFileName(file)), true);
        }

        if (File.Exists(paths.FailureFile))
            File.Copy(paths.FailureFile, Path.Combine(temp, "failures.jsonl"), true);

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) downloads = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(downloads)) downloads = Environment.CurrentDirectory;

        var zip = Path.Combine(downloads, $"AutoEmulatorUpdate-Diagnostic-{stamp}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(temp, zip, CompressionLevel.Optimal, false);
        try { Directory.Delete(temp, true); } catch { }
        return zip;
    }
}
