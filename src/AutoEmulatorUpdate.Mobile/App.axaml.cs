using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AutoEmulatorUpdate.Mobile;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
        {
            mobile.MainView = new MainView
            {
                DataContext = new MobileMainViewModel(new CompanionClient())
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
