using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using MobileApp = AutoEmulatorUpdate.Mobile.App;

namespace AutoEmulatorUpdate.Mobile.Android;

[Application]
public sealed class MobileApplication(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<MobileApp>(javaReference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
