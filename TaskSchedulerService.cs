using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BDSM
{
    public static class TaskSchedulerService
    {
        private static Timer? _timer;
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;
        private static readonly Dictionary<Guid, DateTime> _lastRunTimestamps = new Dictionary<Guid, DateTime>();
        public static ScheduledTask? NextScheduledTask { get; private set; }

        public static bool IsMajorOperationInProgress { get; private set; } = false;

        public static void SetOperationLock() => IsMajorOperationInProgress = true;
        public static void ReleaseOperationLock() => IsMajorOperationInProgress = false;

        public static void ClearLastRunHistory() => _lastRunTimestamps.Clear();

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            UpdateNextScheduledTask(); // Initial calculation
            _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public static void UpdateNextScheduledTask()
        {
            if (_config == null) return;
            NextScheduledTask = _config.Schedules
                .Where(s => s.IsEnabled && s.NextCalculatedRunTime.HasValue)
                .OrderBy(s => s.NextCalculatedRunTime)
                .FirstOrDefault();
        }

        private static void OnTimerTick(object? state)
        {
            UpdateNextScheduledTask();

            if (IsMajorOperationInProgress || _config == null || _appViewModel == null || NextScheduledTask == null)
            {
                return;
            }

            if (DateTime.Now >= NextScheduledTask.NextCalculatedRunTime)
            {
                var taskToRun = NextScheduledTask;
                var thisRunInstance = DateTime.Now.Date + taskToRun.ScheduledTime;
                if (_lastRunTimestamps.ContainsKey(taskToRun.Id) && _lastRunTimestamps[taskToRun.Id] >= thisRunInstance)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    if (IsMajorOperationInProgress) return;
                    SetOperationLock();
                    try
                    {
                        _lastRunTimestamps[taskToRun.Id] = thisRunInstance;
                        Debug.WriteLine($"Executing scheduled task: {taskToRun.Name} of type {taskToRun.TaskType}");
                        var activeServers = _appViewModel.Clusters.SelectMany(c => c.Servers).Where(s => s.IsActive && s.IsInstalled).ToList();
                        switch (taskToRun.TaskType)
                        {
                            case ScheduledTaskType.DailyReboot:
                                await UpdateManager.PerformScheduledRebootAsync(activeServers, _config);
                                break;
                            case ScheduledTaskType.MaintenanceShutdown:
                                await UpdateManager.PerformMaintenanceShutdownAsync(activeServers, _config);
                                break;
                            case ScheduledTaskType.ScheduledBackup:
                                await BackupManager.PerformBackupAsync(activeServers, _config);
                                break;
                        }
                    }
                    finally
                    {
                        ReleaseOperationLock();
                    }
                });
            }
        }
    }
}