using Avalonia;
using Avalonia.iOS;
using Foundation;
using MobileApp = AutoEmulatorUpdate.Mobile.App;

namespace AutoEmulatorUpdate.Mobile.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<MobileApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
