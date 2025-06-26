using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace BDSM
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Create and show the splash screen on the UI thread.
            SplashScreen splashScreen = new SplashScreen();
            splashScreen.Show();

            // 2. Start the main initialization on a background thread.
            Task.Run(async () =>
            {
                var mainViewModel = new ApplicationViewModel();

                // Wait for the long initialization to complete.
                bool wasFirstRun = await mainViewModel.InitializationTask;

                // 3. When done, switch back to the UI thread to transition windows.
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var mainWindow = new MainWindow
                    {
                        DataContext = mainViewModel
                    };
                    mainWindow.Show();

                    // If a new config was just created, guide the user.
                    if (wasFirstRun)
                    {
                        mainViewModel.ShowGlobalSettingsCommand.Execute(null);
                        MessageBox.Show(
                            "Welcome! It looks like this is the first time you've run the server manager.\n\nA new 'config.json' file has been created for you.\n\nPlease go to the 'Global Settings' page to configure important paths, like for SteamCMD and your backups.",
                            "Welcome to BDSM",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    // 4. Close the splash screen now that the main window is visible.
                    splashScreen.Close();
                });
            });
        }
    }
}