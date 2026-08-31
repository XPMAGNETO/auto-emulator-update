using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace AutoEmulatorUpdate.Mobile.Android;

[Activity(
    Label = "Auto Emulator Updater",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/app_icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity;
