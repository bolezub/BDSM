using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BDSM
{
    public static class BackupSchedulerService
    {
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;

        public static DateTime NextBackupTime { get; private set; }

        // The Start method now ONLY calculates the initial NextBackupTime.
        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            CalculateNextBackupTime();
            Debug.WriteLine($"Backup scheduler initialized. Next backup at: {NextBackupTime}");
        }

        // This is the new public method to run the backup and schedule the next one.
        public static async Task RunBackupAndReschedule()
        {
            if (_config == null || _appViewModel == null) return;

            Debug.WriteLine($"Backup initiated. Next backup will be recalculated.");
            var activeAndInstalledServers = _appViewModel.Clusters
                                                           .SelectMany(c => c.Servers)
                                                           .Where(s => s.IsActive && s.IsInstalled)
                                                           .ToList();
            await BackupManager.PerformBackupAsync(activeAndInstalledServers, _config);

            // Reschedule for the next interval.
            CalculateNextBackupTime();
        }

        // A helper method to set the next time.
        private static void CalculateNextBackupTime()
        {
            if (_config == null) return;
            NextBackupTime = DateTime.Now.AddMinutes(Math.Max(1, _config.BackupIntervalMinutes));
        }

        public static void RecalculateNextRunTime()
        {
            CalculateNextBackupTime();
            System.Diagnostics.Debug.WriteLine($"Backup timer reset due to settings change. New next backup at: {NextBackupTime}");
        }
    }
}