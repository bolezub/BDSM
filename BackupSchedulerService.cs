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

            _backupTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        }

        private static void OnTimerTick(object? state)
        {
            _ = Task.Run(async () =>
            {
                if (_config == null || _appViewModel == null) return;

                TimeSpan nextInterval;

                if (TaskSchedulerService.IsMajorOperationInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("Backup attempt skipped: A major operation is in progress.");
                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.BackupRetryIntervalMinutes));
                    System.Diagnostics.Debug.WriteLine($"Will retry backup in {nextInterval.TotalMinutes} minutes.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Starting scheduled backup...");
                    // MODIFIED: Added .Where(s => s.IsInstalled) to filter out uninstalled servers
                    var activeAndInstalledServers = _appViewModel.Clusters
                                                                 .SelectMany(c => c.Servers)
                                                                 .Where(s => s.IsActive && s.IsInstalled)
                                                                 .ToList();

                    await BackupManager.PerformBackupAsync(activeAndInstalledServers, _config);
                    System.Diagnostics.Debug.WriteLine("Scheduled backup finished.");

                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.BackupIntervalMinutes));
                }

                NextBackupTime = DateTime.Now.Add(nextInterval);
                _backupTimer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
                System.Diagnostics.Debug.WriteLine($"Next backup scheduled for: {NextBackupTime}");
            });
        }
    }
}