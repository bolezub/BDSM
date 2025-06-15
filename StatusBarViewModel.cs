using System;
using System.Windows.Threading;

namespace BDSM
{
    public class StatusBarViewModel : BaseViewModel
    {
        private readonly DispatcherTimer _timer;

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


        public StatusBarViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Update Backup Countdown
            var backupTimeRemaining = BackupSchedulerService.NextBackupTime - DateTime.Now;
            if (backupTimeRemaining.TotalSeconds > 0)
                NextBackupText = $"Next Backup: {backupTimeRemaining:hh\\:mm\\:ss}";
            else
                NextBackupText = "Next Backup: In progress or pending...";

            // Update Update Check Countdown
            var updateTimeRemaining = UpdateSchedulerService.NextUpdateCheckTime - DateTime.Now;
            if (updateTimeRemaining.TotalSeconds > 0)
                NextUpdateText = $"Next Update Check: {updateTimeRemaining:hh\\:mm\\:ss}";
            else
                NextUpdateText = "Next Update Check: In progress or pending...";

            // Update Scheduled Task Countdown
            if (TaskSchedulerService.NextScheduledTask?.NextCalculatedRunTime is { } nextRun)
            {
                var taskTimeRemaining = nextRun - DateTime.Now;
                if (taskTimeRemaining.TotalSeconds > 0)
                    NextScheduledTaskText = $"Next Task ({TaskSchedulerService.NextScheduledTask.Name}): {taskTimeRemaining:hh\\:mm\\:ss}";
                else
                    NextScheduledTaskText = $"Next Task ({TaskSchedulerService.NextScheduledTask.Name}): Starting...";
            }
            else
            {
                NextScheduledTaskText = "No further tasks scheduled.";
            }
        }
    }
}