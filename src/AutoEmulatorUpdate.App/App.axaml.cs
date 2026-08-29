using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AutoEmulatorUpdate.Core.Models;
using AutoEmulatorUpdate.Core.Services;

namespace AutoEmulatorUpdate.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var store = new JsonStore();
            var settings = await store.LoadAsync(paths.SettingsFile, new AppSettings());

            if (!settings.FirstRunCompleted)
            {
                var wizard = new FirstRunWindow();
                wizard.DataContext = await FirstRunWindowViewModel.CreateAsync(wizard);
                await wizard.ShowDialog<bool?>(null!);
                settings = await store.LoadAsync(paths.SettingsFile, new AppSettings());
            }

            var main = new MainWindow();
            var vm = new MainWindowViewModel(main);
            main.DataContext = vm;
            desktop.MainWindow = main;

            if (settings.BackgroundMode)
            {
                main.Closing += (_, e) =>
                {
                    e.Cancel = true;
                    main.Hide();
                };
            }

            main.Show();
            await vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
