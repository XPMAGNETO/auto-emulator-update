namespace AutoEmulatorUpdate.Core.Models;

public enum UpdateChannel { Stable, Beta, Development }
public enum QueueAction { Update, Install, Repair, DownloadOnly }
public enum QueueState { Queued, Running, Paused, Complete, Failed, Cancelled }
public enum SourceHealthState { Unknown, Healthy, Fallback, Failed }
public enum ManagementOwner { AutoEmulatorUpdate, Frontend }
public enum MaintenanceMode { CheckOnly, AskBeforeInstall, DownloadOnly, BackupUpdateVerify }
public enum StartupBehavior { Manual, CheckOnLaunch, UpdateOnLaunch }
public enum UserExperienceMode { Simple, Advanced }

public sealed record PlatformPackage(
    string Os,
    string Arch,
    string AssetPattern,
    string? DirectUrlTemplate = null,
    string? ArchiveType = null);

public sealed record ReleaseSource(
    string Kind,
    string? Repository = null,
    string? Url = null,
    string? VersionRegex = null,
    string? VersionJsonPath = null,
    string? DownloadJsonPath = null,
    string? Channel = null);

public sealed class EmulatorDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public Dictionary<string, string[]> Executables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PlatformPackage> Packages { get; init; } = [];
    public List<ReleaseSource> Sources { get; init; } = [];
    public string[] ProtectedPaths { get; init; } = [];
    public string[] SavePaths { get; init; } = [];
    public string[] DependencyHints { get; init; } = [];
    public bool Legacy { get; init; }
}

public sealed class InstalledEmulator
{
    public required EmulatorDefinition Definition { get; init; }
    public required string InstallPath { get; set; }
    public string? ExecutablePath { get; set; }
    public string CurrentVersion { get; set; } = "Unknown";
    public string LatestVersion { get; set; } = "Unknown";
    public string DetectionMethod { get; set; } = "Unknown";
    public string Confidence { get; set; } = "Low";
    public string FrontendOwner { get; set; } = "Manual / Scan";
    public bool IsPortable { get; set; }
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    public string? PinnedVersion { get; set; }
    public string? IgnoredVersion { get; set; }
    public ManagementOwner ManagementOwner { get; set; } = ManagementOwner.AutoEmulatorUpdate;
    public SourceHealthState SourceHealth { get; set; }
    public string SourceName { get; set; } = "";
    public string Status { get; set; } = "Detected";
    public bool Selected { get; set; }
    public string ProfileName { get; set; } = "Primary";
}

public sealed record ReleaseInfo(
    string Version,
    string DownloadUrl,
    string AssetName,
    string Source,
    long? SizeBytes = null,
    string? Sha256 = null,
    string? ReleaseNotes = null,
    string? ReleaseUrl = null,
    UpdateChannel Channel = UpdateChannel.Stable);

public sealed class UpdateQueueItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required InstalledEmulator Emulator { get; init; }
    public QueueAction Action { get; init; }
    public QueueState State { get; set; } = QueueState.Queued;
    public double Progress { get; set; }
    public string Message { get; set; } = "Queued";
}

public sealed record HistoryEntry(
    DateTimeOffset Timestamp,
    string Emulator,
    string Action,
    string? OldVersion,
    string? NewVersion,
    string Result,
    string? Source,
    string? BackupPath);

public sealed record FailureEntry(
    DateTimeOffset Timestamp,
    string Emulator,
    string Stage,
    string Message,
    string? Source);

public sealed record BackupRecord(
    string Emulator,
    string Version,
    DateTimeOffset Timestamp,
    string Path);

public sealed record LaunchValidationResult(bool Success, string Message, int? ExitCode = null);

public sealed class AppSettings
{
    public bool AutoScanDrives { get; set; } = true;
    public bool AutoCloseAfterRun { get; set; }
    public int ParallelChecks { get; set; } = 4;
    public int BackupRetentionDays { get; set; } = 30;
    public double BackupMaxGb { get; set; } = 10;
    public int DownloadCacheDays { get; set; } = 14;
    public double BandwidthLimitMBps { get; set; }
    public bool VerifyChecksums { get; set; } = true;
    public bool VerifySignatures { get; set; } = true;
    public bool AutoRollbackOnValidationFailure { get; set; } = true;
    public bool PostUpdateLaunchTest { get; set; } = true;
    public int PostUpdateLaunchTestSeconds { get; set; } = 4;
    public bool NotificationsEnabled { get; set; } = true;
    public bool FrontendProtectionDefault { get; set; } = true;
    public MaintenanceMode MaintenanceMode { get; set; } = MaintenanceMode.BackupUpdateVerify;
    public StartupBehavior StartupBehavior { get; set; } = StartupBehavior.CheckOnLaunch;
    public UserExperienceMode ExperienceMode { get; set; } = UserExperienceMode.Simple;
    public bool FirstRunCompleted { get; set; }
    public bool BackgroundMode { get; set; }
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool AutoAppUpdates { get; set; } = true;
    public bool ScheduleEnabled { get; set; }
    public string ScheduleTime { get; set; } = "09:00";
    public DayOfWeek[] ScheduleDays { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
    public string DefaultInstallRoot { get; set; } = "";
    public string? CatalogManifestUrl { get; set; }
    public string? SelfUpdateManifestUrl { get; set; }
}
