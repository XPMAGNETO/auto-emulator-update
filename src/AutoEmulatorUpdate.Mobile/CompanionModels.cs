namespace AutoEmulatorUpdate.Mobile;

public sealed record PairRequest(string Code, string DeviceName);
public sealed record PairResponse(string AccessToken, CompanionSnapshot Snapshot);
public sealed record CompanionCommand(string Name);

public sealed record CompanionSnapshot(
    int InstalledCount,
    int UpdateCount,
    string StatusMessage,
    IReadOnlyList<CompanionEmulator> Emulators,
    IReadOnlyList<CompanionActivity> Activity)
{
    public static CompanionSnapshot Empty { get; } = new(0, 0, "Pair with your computer to begin.", [], []);
}

public sealed record CompanionEmulator(string Name, string CurrentVersion, string AvailableVersion, string Status)
{
    public string VersionSummary => $"{CurrentVersion} → {AvailableVersion}";
}

public sealed record CompanionActivity(DateTimeOffset Timestamp, string Message)
{
    public string TimestampLabel => Timestamp.LocalDateTime.ToString("g");
}
