namespace AutoEmulatorUpdate.Core.Services;

public sealed class AppPaths
{
    public string DataRoot { get; }
    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public string HistoryFile => Path.Combine(DataRoot, "history.jsonl");
    public string FailureFile => Path.Combine(DataRoot, "failures.jsonl");
    public string BackupsRoot => Path.Combine(DataRoot, "backups");
    public string SaveBackupsRoot => Path.Combine(DataRoot, "save-backups");
    public string CacheRoot => Path.Combine(DataRoot, "cache");
    public string UserManifestRoot => Path.Combine(DataRoot, "manifests");
    public string LogsRoot => Path.Combine(DataRoot, "logs");
    public string CompanionCertificateFile => Path.Combine(DataRoot, "companion-identity.pfx");
    public string CompanionDevicesFile => Path.Combine(DataRoot, "companion-devices.json");

    public AppPaths()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (OperatingSystem.IsLinux())
            baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        DataRoot = Path.Combine(baseDir, "AutoEmulatorUpdate");
        foreach (var p in new[] { DataRoot, BackupsRoot, SaveBackupsRoot, CacheRoot, UserManifestRoot, LogsRoot })
            Directory.CreateDirectory(p);
    }
}
