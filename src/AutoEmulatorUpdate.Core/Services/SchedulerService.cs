using System.Diagnostics;
using System.Text;

namespace AutoEmulatorUpdate.Core.Services;

public sealed record ScheduleDefinition(bool Enabled, TimeOnly Time, DayOfWeek[] Days, string ExecutablePath);

public interface ISchedulerService
{
    Task ApplyAsync(ScheduleDefinition schedule, CancellationToken ct = default);
    Task DisableAsync(CancellationToken ct = default);
}

public sealed class CrossPlatformSchedulerService : ISchedulerService
{
    public Task ApplyAsync(ScheduleDefinition s, CancellationToken ct = default) =>
        OperatingSystem.IsWindows() ? WindowsAsync(s, ct) :
        OperatingSystem.IsMacOS() ? MacAsync(s, ct) : LinuxAsync(s, ct);

    public Task DisableAsync(CancellationToken ct = default) =>
        OperatingSystem.IsWindows() ? RunAsync("schtasks", "/Delete /TN \"Auto Emulator Update\" /F", ct, ignoreExit: true) :
        OperatingSystem.IsMacOS() ? DisableMacAsync(ct) : DisableLinuxAsync(ct);

    private static async Task WindowsAsync(ScheduleDefinition s, CancellationToken ct)
    {
        await RunAsync("schtasks", "/Delete /TN \"Auto Emulator Update\" /F", ct, true);
        var days = string.Join(",", s.Days.Select(d => d.ToString()[..3].ToUpperInvariant()));
        var args = $"/Create /TN \"Auto Emulator Update\" /SC WEEKLY /D {days} /ST {s.Time:HH\\:mm} /TR \"\\\"{s.ExecutablePath}\\\" --scheduled\" /F";
        await RunAsync("schtasks", args, ct);
    }

    private static async Task LinuxAsync(ScheduleDefinition s, CancellationToken ct)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".config", "systemd", "user");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "auto-emulator-update.service"),
            $"[Unit]\nDescription=Auto Emulator Update\n[Service]\nType=oneshot\nExecStart=\"{s.ExecutablePath}\" --scheduled\n", ct);
        var days = string.Join(",", s.Days.Select(d => d.ToString()[..3]));
        await File.WriteAllTextAsync(Path.Combine(dir, "auto-emulator-update.timer"),
            $"[Unit]\nDescription=Auto Emulator Update schedule\n[Timer]\nOnCalendar={days} *-*-* {s.Time:HH\\:mm}:00\nPersistent=true\n[Install]\nWantedBy=timers.target\n", ct);
        await RunAsync("systemctl", "--user daemon-reload", ct);
        await RunAsync("systemctl", "--user enable --now auto-emulator-update.timer", ct);
    }

    private static async Task MacAsync(ScheduleDefinition s, CancellationToken ct)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "Library", "LaunchAgents", "com.autoemulatorupdate.schedule.plist");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var intervals = new StringBuilder();
        foreach (var day in s.Days)
        {
            var weekday = day == DayOfWeek.Sunday ? 1 : (int)day + 1;
            intervals.Append($"""
              <dict><key>Weekday</key><integer>{weekday}</integer><key>Hour</key><integer>{s.Time.Hour}</integer><key>Minute</key><integer>{s.Time.Minute}</integer></dict>
            """);
        }
        var plist = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0"><dict>
          <key>Label</key><string>com.autoemulatorupdate.schedule</string>
          <key>ProgramArguments</key><array><string>{s.ExecutablePath}</string><string>--scheduled</string></array>
          <key>StartCalendarInterval</key><array>{intervals}</array>
        </dict></plist>
        """;
        await File.WriteAllTextAsync(path, plist, ct);
        await RunAsync("launchctl", $"unload \"{path}\"", ct, true);
        await RunAsync("launchctl", $"load \"{path}\"", ct);
    }

    private static async Task DisableLinuxAsync(CancellationToken ct)
        => await RunAsync("systemctl", "--user disable --now auto-emulator-update.timer", ct, true);

    private static async Task DisableMacAsync(CancellationToken ct)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "com.autoemulatorupdate.schedule.plist");
        await RunAsync("launchctl", $"unload \"{path}\"", ct, true);
        try { File.Delete(path); } catch { }
    }

    private static async Task RunAsync(string exe, string args, CancellationToken ct, bool ignoreExit = false)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Unable to start {exe}.");
        await p.WaitForExitAsync(ct);
        if (!ignoreExit && p.ExitCode != 0) throw new InvalidOperationException(await p.StandardError.ReadToEndAsync(ct));
    }
}
