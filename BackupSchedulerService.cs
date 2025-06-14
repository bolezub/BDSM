using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BDSM
{
    public static class BackupSchedulerService
    {
        private static Timer? _backupTimer;
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;

        public static DateTime NextBackupTime { get; private set; }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;

            // The timer will be started by the first Tick.
            // We use a one-shot timer that reschedules itself after every run.
            _backupTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        }

        private static void OnTimerTick(object? state)
        {
            // This entire block runs in a background thread to not block the UI or the timer itself.
            _ = Task.Run(async () =>
            {
                if (_config == null || _appViewModel == null) return;

                TimeSpan nextInterval;

                // Check if another major operation is already running.
                if (TaskSchedulerService.IsMajorOperationInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("Backup attempt skipped: A major operation is in progress.");
                    // Set the timer for a short retry interval.
                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.BackupRetryIntervalMinutes));
                    System.Diagnostics.Debug.WriteLine($"Will retry backup in {nextInterval.TotalMinutes} minutes.");
                }
                else
                {
                    // If we are clear to run, perform the backup.
                    System.Diagnostics.Debug.WriteLine("Starting scheduled backup...");
                    var activeServers = _appViewModel.Servers.Where(s => s.IsActive).ToList();
                    await BackupManager.PerformBackupAsync(activeServers, _config);
                    System.Diagnostics.Debug.WriteLine("Scheduled backup finished.");

                    // After a successful backup, set the timer for the normal, longer interval.
                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.BackupIntervalMinutes));
                }

                // Update the UI property and reschedule the one-shot timer for the next run.
                NextBackupTime = DateTime.Now.Add(nextInterval);
                _backupTimer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
                System.Diagnostics.Debug.WriteLine($"Next backup scheduled for: {NextBackupTime}");
            });
        }
    }
}