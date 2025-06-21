using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BDSM
{
    public static class UpdateSchedulerService
    {
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;
        public static DateTime NextUpdateCheckTime { get; private set; }

        // The Start method now ONLY calculates the initial NextUpdateCheckTime.
        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            CalculateNextUpdateCheckTime();
            Debug.WriteLine($"Update scheduler initialized. Next check at: {NextUpdateCheckTime}");
        }

        // This is the new public method to run the update check and schedule the next one.
        public static async Task RunUpdateCheckAndReschedule()
        {
            if (_config == null || _appViewModel == null) return;

            Debug.WriteLine($"Update check initiated. Next check will be recalculated.");
            await _appViewModel.CheckAllServersForUpdate();
            var serversToUpdate = _appViewModel.Clusters
                                               .SelectMany(c => c.Servers)
                                               .Where(s => s.IsInstalled && s.IsUpdateAvailable)
                                               .ToList();
            if (serversToUpdate.Any())
            {
                await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
            }

            // Reschedule for the next interval.
            CalculateNextUpdateCheckTime();
        }

        // A helper method to set the next time.
        private static void CalculateNextUpdateCheckTime()
        {
            if (_config == null) return;
            NextUpdateCheckTime = DateTime.Now.AddMinutes(Math.Max(1, _config.UpdateCheckIntervalMinutes));
        }

        public static void RecalculateNextRunTime()
        {
            CalculateNextUpdateCheckTime();
            System.Diagnostics.Debug.WriteLine($"Update timer reset due to settings change. New next check at: {NextUpdateCheckTime}");
        }
    }
}