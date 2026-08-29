using System.Diagnostics;

namespace AutoEmulatorUpdate.Core.Services;

public sealed class NotificationService
{
    public async Task ShowAsync(string title, string message, CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                await RunAsync("notify-send", $"\"{Escape(title)}\" \"{Escape(message)}\"", ct);
            else if (OperatingSystem.IsMacOS())
                await RunAsync("osascript", $"-e \"display notification \\\"{Escape(message)}\\\" with title \\\"{Escape(title)}\\\"\"", ct);
            else if (OperatingSystem.IsWindows())
            {
                var script = $"[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms')|Out-Null;[System.Windows.Forms.MessageBox]::Show('{message.Replace("'","''")}','{title.Replace("'","''")}')|Out-Null";
                await RunAsync("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script.Replace("\"","\\\"")}\"", ct);
            }
        }
        catch { }
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");
    private static async Task RunAsync(string exe, string args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        if (p is not null) await p.WaitForExitAsync(ct);
    }
}
