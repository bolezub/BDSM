using System;
using System.Windows.Threading;

namespace BDSM
{
    public class StatusBarViewModel : BaseViewModel
    {
        private string _latestBuildText = "Latest Build: checking...";
        public string LatestBuildText
        {
            get => _latestBuildText;
            set { _latestBuildText = value; OnPropertyChanged(); }
        }

        private string _nextBackupText = "Calculating...";
        public string NextBackupText
        {
            get => _nextBackupText;
            set { _nextBackupText = value; OnPropertyChanged(); }
        }

        private string _nextUpdateText = "Calculating...";
        public string NextUpdateText
        {
            get => _nextUpdateText;
            set { _nextUpdateText = value; OnPropertyChanged(); }
        }

        private string _nextScheduledTaskText = "No tasks scheduled.";
        public string NextScheduledTaskText
        {
            get => _nextScheduledTaskText;
            set { _nextScheduledTaskText = value; OnPropertyChanged(); }
        }

        // Timer is declared but not initialized here.
        private DispatcherTimer? _timer;

        public StatusBarViewModel()
        {
            // The constructor is now empty.
        }

        // New method to be called when the UI is ready.
        public void StartTimer()
        {
            // If timer already running, do nothing.
            if (_timer != null && _timer.IsEnabled)
                return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // --- Part 1: Update UI Countdown Timers (Existing Logic) ---

                // Update Backup Countdown
                var backupTimeRemaining = BackupSchedulerService.NextBackupTime - DateTime.Now;
                if (backupTimeRemaining.TotalSeconds > -1 && BackupSchedulerService.NextBackupTime != default)
                {
                    NextBackupText = backupTimeRemaining.TotalSeconds > 0 ? $"Next Backup: {backupTimeRemaining:hh\\:mm\\:ss}" : "Next Backup: In progress...";
                }
                else
                {
                    NextBackupText = "Calculating...";
                }

                // Update Update Check Countdown
                var updateTimeRemaining = UpdateSchedulerService.NextUpdateCheckTime - DateTime.Now;
                if (updateTimeRemaining.TotalSeconds > -1 && UpdateSchedulerService.NextUpdateCheckTime != default)
                {
                    NextUpdateText = updateTimeRemaining.TotalSeconds > 0 ? $"Next Update Check: {updateTimeRemaining:hh\\:mm\\:ss}" : "Next Update Check: In progress...";
                }
                else
                {
                    NextUpdateText = "Calculating...";
                }

                // Update Scheduled Task Countdown
                if (TaskSchedulerService.NextScheduledTask?.NextCalculatedRunTime is { } nextRun)
                {
                    var taskTimeRemaining = nextRun - DateTime.Now;
                    NextScheduledTaskText = taskTimeRemaining.TotalSeconds > 0 ? $"Next Task ({TaskSchedulerService.NextScheduledTask.Name}): {taskTimeRemaining:hh\\:mm\\:ss}" : $"Next Task ({TaskSchedulerService.NextScheduledTask.Name}): Starting...";
                }
                else
                {
                    NextScheduledTaskText = "No further tasks scheduled.";
                }

                // --- Part 2: Trigger Scheduled Tasks (NEWLY ADDED LOGIC) ---

                // Check if it's time to run the backup
                if (DateTime.Now >= BackupSchedulerService.NextBackupTime && !TaskSchedulerService.IsMajorOperationInProgress)
                {
                    _ = BackupSchedulerService.RunBackupAndReschedule();
                }

                // Check if it's time to run the update check
                if (DateTime.Now >= UpdateSchedulerService.NextUpdateCheckTime && !TaskSchedulerService.IsMajorOperationInProgress)
                {
                    _ = UpdateSchedulerService.RunUpdateCheckAndReschedule();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! ERROR in StatusBar timer: {ex.Message}");
            }
        }
    }
}