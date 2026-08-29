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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = InitializeDesktopAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeDesktopAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var paths = new AppPaths();
        var log = new AppLogService(paths);

        try
        {
            var store = new JsonStore();
            AppSettings settings;

            if (Program.SmokeTestMode || Program.FirstRunSmokeTestMode)
            {
                settings = new AppSettings
                {
                    FirstRunCompleted = !Program.FirstRunSmokeTestMode,
                    StartupBehavior = StartupBehavior.Manual,
                    AutoAppUpdates = false,
                    BackgroundMode = false,
                    NotificationsEnabled = false
                };
                await store.SaveAsync(paths.SettingsFile, settings);
            }
            else
            {
                settings = await store.LoadAsync(paths.SettingsFile, new AppSettings());
            }

            if (!settings.FirstRunCompleted)
            {
                var wizard = new FirstRunWindow();
                wizard.DataContext = await FirstRunWindowViewModel.CreateAsync(wizard);
                desktop.MainWindow = wizard;

                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wizard.Closed += (_, _) => closed.TrySetResult();
                wizard.Show();

                if (Program.FirstRunSmokeTestMode)
                {
                    // Exercise the real installed first-run window, then automatically continue
                    // so CI can verify that a brand-new installation reaches the dashboard.
                    await Task.Delay(1000);
                    wizard.Close();
                    settings.FirstRunCompleted = true;
                    await store.SaveAsync(paths.SettingsFile, settings);
                }

                await closed.Task;
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
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            await vm.InitializeAsync();

            if (Program.SmokeTestMode || Program.FirstRunSmokeTestMode)
            {
                await Task.Delay(750);
                var kind = Program.FirstRunSmokeTestMode ? "FIRST-RUN SMOKE TEST" : "SMOKE TEST";
                await log.WriteAsync($"{kind} PASSED: application reached the main window successfully.");
                desktop.Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await log.WriteAsync($"FATAL STARTUP ERROR: {ex}");
            }
            catch
            {
                // Never hide the original startup failure because logging also failed.
            }

            if (Program.SmokeTestMode || Program.FirstRunSmokeTestMode)
            {
                Console.Error.WriteLine(ex);
                desktop.Shutdown(1);
                return;
            }

            var errorWindow = new Window
            {
                Title = "Auto Emulator Update - Startup Error",
                Width = 640,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Auto Emulator Update could not start.",
                            FontSize = 22,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = ex.Message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"A diagnostic startup log was written under: {paths.LogsRoot}",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Opacity = 0.75
                        }
                    }
                }
            };

            desktop.MainWindow = errorWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            errorWindow.Show();
        }
    }
}
