using Avalonia;

namespace AutoEmulatorUpdate.App;

internal static class Program
{
    public static bool SmokeTestMode { get; private set; }
    public static bool FirstRunSmokeTestMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        SmokeTestMode = args.Any(arg =>
            arg.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        FirstRunSmokeTestMode = args.Any(arg =>
            arg.Equals("--first-run-smoke-test", StringComparison.OrdinalIgnoreCase));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
