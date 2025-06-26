using System;
using System.Linq;
using System.Threading.Tasks;

namespace BDSM
{
    public static class BackupSchedulerService
    {
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;

        public static DateTime NextBackupTime { get; private set; }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            CalculateNextBackupTime();
        }

        public static async Task RunBackupAndReschedule()
        {
            if (_config == null || _appViewModel == null) return;

            var activeAndInstalledServers = _appViewModel.Clusters
                                                           .SelectMany(c => c.Servers)
                                                           .Where(s => s.IsActive && s.IsInstalled)
                                                           .ToList();
            await BackupManager.PerformBackupAsync(activeAndInstalledServers, _config);

            CalculateNextBackupTime();
        }

        private static void CalculateNextBackupTime()
        {
            if (_config == null) return;
            NextBackupTime = DateTime.Now.AddMinutes(Math.Max(1, _config.BackupIntervalMinutes));
        }

        public static void RecalculateNextRunTime()
        {
            CalculateNextBackupTime();
        }
    }
}