using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace BDSM
{
    public partial class App : Application
    {
        // FIX: The OnStartup method is no longer async.
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // This part happens on the UI thread:
            var splashScreen = new SplashScreen();
            splashScreen.Show();

            // FIX: We kick off all the loading work on a separate background thread.
            Task.Run(async () =>
            {
                // --- This entire block now runs on a background thread ---
                // --- This will not freeze the splash screen video ---

                var minimumDisplayTime = TimeSpan.FromSeconds(2);
                var stopwatch = Stopwatch.StartNew();

                // Create the main view model and start its async data loading.
                var mainViewModel = new ApplicationViewModel();

                // Wait here until the main view model's InitializationTask is complete.
                await mainViewModel.InitializationTask;

                // Stop the stopwatch and see how long loading took.
                stopwatch.Stop();
                var loadTime = stopwatch.Elapsed;

                // If loading was faster than our minimum, wait for the remainder.
                if (loadTime < minimumDisplayTime)
                {
                    await Task.Delay(minimumDisplayTime - loadTime);
                }

                // --- Background work is done. Now we must return to the UI thread ---
                // --- to create and show UI elements. We use the Dispatcher for this. ---
                splashScreen.Dispatcher.Invoke(() =>
                {
                    var mainWindow = new MainWindow
                    {
                        DataContext = mainViewModel
                    };
                    mainWindow.Show();

                    splashScreen.Close();
                });
            });
        }
    }
}