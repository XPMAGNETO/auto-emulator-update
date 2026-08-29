namespace AutoEmulatorUpdate.Core;

public static class BuildInfo
{
    public const string Version = "10.1.0-alpha.1";

    // The GitHub setup script replaces OWNER with the authenticated GitHub user.
    // Release builds can then self-update from the repository's Releases feed.
    public const string GitHubRepository = "xpm69420/auto-emulator-update";

    public static bool HasConfiguredRepository =>
        !GitHubRepository.StartsWith("OWNER/", StringComparison.OrdinalIgnoreCase);
}
