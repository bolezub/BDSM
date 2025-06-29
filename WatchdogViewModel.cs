using Newtonsoft.Json;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BDSM
{
    public class WatchdogViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private readonly DispatcherTimer _timer;

        private string _timeUntilNextScan = "Calculating...";
        public string TimeUntilNextScan
        {
            get => _timeUntilNextScan;
            set { _timeUntilNextScan = value; OnPropertyChanged(); }
        }

        private string _timeUntilNextGraphPost = "Calculating...";
        public string TimeUntilNextGraphPost
        {
            get => _timeUntilNextGraphPost;
            set { _timeUntilNextGraphPost = value; OnPropertyChanged(); }
        }

        public bool IsEnabled
        {
            get => _config.Watchdog.IsEnabled;
            set { _config.Watchdog.IsEnabled = value; OnPropertyChanged(); }
        }

        public int ScanIntervalSeconds
        {
            get => _config.Watchdog.ScanIntervalSeconds;
            set { _config.Watchdog.ScanIntervalSeconds = value; OnPropertyChanged(); }
        }

        public int GraphPostIntervalMinutes
        {
            get => _config.Watchdog.GraphPostIntervalMinutes;
            set { _config.Watchdog.GraphPostIntervalMinutes = value; OnPropertyChanged(); }
        }

        public ICommand SaveWatchdogSettingsCommand { get; }

        // --- NEW COMMAND ---
        public ICommand OpenDebugWindowCommand { get; }

        public WatchdogViewModel(GlobalConfig globalConfig)
        {
            _config = globalConfig;
            SaveWatchdogSettingsCommand = new RelayCommand(_ => SaveSettings());

            // --- NEW COMMAND INITIALIZATION ---
            OpenDebugWindowCommand = new RelayCommand(_ => {
                var debugWindow = new DiscordDebugWindow
                {
                    DataContext = new DiscordDebugViewModel(_config)
                };
                debugWindow.Show();
            });

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_config.Watchdog.IsEnabled)
            {
                TimeUntilNextScan = "Watchdog is disabled.";
                TimeUntilNextGraphPost = "Watchdog is disabled.";
                return;
            }

            var scanTimeRemaining = WatchdogService.NextScanTime - DateTime.Now;
            if (scanTimeRemaining.TotalSeconds > 0)
                TimeUntilNextScan = $"Next server scan in: {scanTimeRemaining:hh\\:mm\\:ss}";
            else
                TimeUntilNextScan = "Next server scan: In progress or pending...";

            var graphTimeRemaining = WatchdogService.NextGraphPostTime - DateTime.Now;
            if (graphTimeRemaining.TotalSeconds > 0)
                TimeUntilNextGraphPost = $"Next graph post in: {graphTimeRemaining:hh\\:mm\\:ss}";
            else
                TimeUntilNextGraphPost = "Next graph post: In progress or pending...";
        }

        private void SaveSettings()
        {
            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);

                WatchdogService.RestartTimer();

                NotificationService.ShowInfo("Watchdog settings saved and applied immediately.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save Watchdog settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
