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

            // Use a one-shot timer that reschedules itself after each operation.
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
                    // Use the same retry interval as backups for simplicity.
                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.BackupRetryIntervalMinutes));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Checking all servers for updates...");
                    await _appViewModel.CheckAllServersForUpdate();

                    var serversToUpdate = _appViewModel.Servers.Where(s => s.IsUpdateAvailable).ToList();

                    if (serversToUpdate.Any())
                    {
                        System.Diagnostics.Debug.WriteLine($"{serversToUpdate.Count} server(s) require an update. Starting update process...");
                        // This will set the operation lock internally
                        await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
                        System.Diagnostics.Debug.WriteLine("Automatic update process finished.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("All servers are up-to-date.");
                    }

                    // After a successful check/run, set the timer for the normal interval.
                    nextInterval = TimeSpan.FromMinutes(Math.Max(1, _config.UpdateCheckIntervalMinutes));
                }

                // Update the public property and reschedule the timer.
                NextUpdateCheckTime = DateTime.Now.Add(nextInterval);
                _updateTimer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
                System.Diagnostics.Debug.WriteLine($"Next update check scheduled in {nextInterval.TotalMinutes} minutes.");
            });
        }
    }
}