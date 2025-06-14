using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace BDSM
{
    public class BackupViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private readonly IEnumerable<ServerViewModel> _servers;
        private readonly DispatcherTimer _countdownTimer;
        private string _timeUntilNextBackup = "";

        public ICommand StartManualBackupCommand { get; }

        public string TimeUntilNextBackup
        {
            get => _timeUntilNextBackup;
            set { _timeUntilNextBackup = value; OnPropertyChanged(); }
        }

        public BackupViewModel(GlobalConfig config, IEnumerable<ServerViewModel> servers)
        {
            _config = config;
            _servers = servers;

            StartManualBackupCommand = new RelayCommand(
                async _ => await RunBackup(),
                _ => !TaskSchedulerService.IsMajorOperationInProgress
            );

            // Set up the 1-second timer for the UI countdown
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            var timeRemaining = BackupSchedulerService.NextBackupTime - DateTime.Now;
            if (timeRemaining.TotalSeconds > 0)
            {
                TimeUntilNextBackup = $"Next automatic backup in: {timeRemaining:hh\\:mm\\:ss}";
            }
            else
            {
                TimeUntilNextBackup = "Next automatic backup is starting...";
            }
        }

        private async Task RunBackup()
        {
            var activeServers = _servers.Where(s => s.IsActive).ToList();
            await BackupManager.PerformBackupAsync(activeServers, _config);
        }
    }
}