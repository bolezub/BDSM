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

            Task.Run(async () =>
            {
                var mainViewModel = new ApplicationViewModel();

                // Wait for initialization and get the result.
                bool wasFirstRun = await mainViewModel.InitializationTask;

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
                });
            });
        }
    }
}