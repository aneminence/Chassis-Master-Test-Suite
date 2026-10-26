using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Chassis_Master_Test_Suite.Themes;

namespace Chassis_Master_Test_Suite
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var startedAt = Stopwatch.StartNew();
            var splash = new SplashWindow();

            try
            {
                splash.Show();
                splash.Report(5);
                await Dispatcher.Yield(DispatcherPriority.Render);

                AppearanceService.Initialize();
                splash.Report(18);
                await Dispatcher.Yield(DispatcherPriority.Background);

                splash.Report(28);
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                splash.Report(55);

                await mainWindow.InitializeStartupAsync(splash);

                var remaining = TimeSpan.FromMilliseconds(1800) - startedAt.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);

                splash.Report(100);
                await splash.FadeOutAsync(TimeSpan.FromMilliseconds(250));
                splash.Close();

                // The main window is intentionally shown for the first time only
                // after the splash has completely closed.
                mainWindow.Show();
                mainWindow.Activate();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.ShowStartupWarningIfAny();
            }
            catch (Exception ex)
            {
                splash.Close();
                MessageBox.Show(
                    $"CMTS failed to start.\n\n{ex.Message}",
                    "CMTS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            }
        }
    }
}
