using System;
using System.Linq;
using System.Threading.Tasks;

namespace BDSM
{
    public static class UpdateSchedulerService
    {
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;
        public static DateTime NextUpdateCheckTime { get; private set; }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            CalculateNextUpdateCheckTime();
        }

        public static async Task RunUpdateCheckAndReschedule()
        {
            if (_config == null || _appViewModel == null) return;

            await _appViewModel.CheckAllServersForUpdate();
            var serversToUpdate = _appViewModel.Clusters
                                               .SelectMany(c => c.Servers)
                                               .Where(s => s.IsInstalled && s.IsUpdateAvailable)
                                               .ToList();
            if (serversToUpdate.Any())
            {
                await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
            }

            CalculateNextUpdateCheckTime();
        }

        private static void CalculateNextUpdateCheckTime()
        {
            if (_config == null) return;
            NextUpdateCheckTime = DateTime.Now.AddMinutes(Math.Max(1, _config.UpdateCheckIntervalMinutes));
        }

        public static void RecalculateNextRunTime()
        {
            CalculateNextUpdateCheckTime();
        }
    }
}