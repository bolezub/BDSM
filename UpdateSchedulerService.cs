using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BDSM
{
    public static class UpdateSchedulerService
    {
        private static Timer? _updateTimer;
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;
        public static DateTime NextUpdateCheckTime { get; private set; }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;

            _updateTimer = new Timer(OnTimerTick, null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
            System.Diagnostics.Debug.WriteLine($"Update scheduler started.");
        }

        private static void OnTimerTick(object? state)
        {
            _ = Task.Run(async () =>
            {
                if (_config == null || _appViewModel == null) return;

                TimeSpan nextInterval;

                if (TaskSchedulerService.IsMajorOperationInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("Update check skipped: A major operation is in progress.");
                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.BackupRetryIntervalMinutes));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Checking all servers for updates...");
                    await _appViewModel.CheckAllServersForUpdate();

                    // MODIFIED: Added .Where(s => s.IsInstalled) to filter out uninstalled servers
                    var serversToUpdate = _appViewModel.Clusters
                                                       .SelectMany(c => c.Servers)
                                                       .Where(s => s.IsInstalled && s.IsUpdateAvailable)
                                                       .ToList();

                    if (serversToUpdate.Any())
                    {
                        System.Diagnostics.Debug.WriteLine($"{serversToUpdate.Count} server(s) require an update. Starting update process...");
                        await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
                        System.Diagnostics.Debug.WriteLine("Automatic update process finished.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("All servers are up-to-date.");
                    }

                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.UpdateCheckIntervalMinutes));
                }

                NextUpdateCheckTime = DateTime.Now.Add(nextInterval);
                _updateTimer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
                System.Diagnostics.Debug.WriteLine($"Next update check scheduled in {nextInterval.TotalMinutes} minutes.");
            });
        }
    }
}