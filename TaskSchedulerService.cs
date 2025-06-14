using System;
using System.Collections.Generic;
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

        public static bool IsMajorOperationInProgress { get; private set; } = false;

        public static void SetOperationLock() => IsMajorOperationInProgress = true;
        public static void ReleaseOperationLock() => IsMajorOperationInProgress = false;

        public static void ClearLastRunHistory() => _lastRunTimestamps.Clear();

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        }

        private static bool IsScheduledForToday(ScheduledTask task, DayOfWeek today)
        {
            switch (today)
            {
                case DayOfWeek.Monday: return task.RunsOnMonday;
                case DayOfWeek.Tuesday: return task.RunsOnTuesday;
                case DayOfWeek.Wednesday: return task.RunsOnWednesday;
                case DayOfWeek.Thursday: return task.RunsOnThursday;
                case DayOfWeek.Friday: return task.RunsOnFriday;
                case DayOfWeek.Saturday: return task.RunsOnSaturday;
                case DayOfWeek.Sunday: return task.RunsOnSunday;
                default: return false;
            }
        }

        private static void OnTimerTick(object? state)
        {
            if (IsMajorOperationInProgress || _config == null || _appViewModel == null)
            {
                return;
            }

            var now = DateTime.Now;

            var tasksToRun = _config.Schedules.Where(task =>
            {
                var thisRunInstance = now.Date + task.ScheduledTime;
                bool hasRunForThisInstance = _lastRunTimestamps.TryGetValue(task.Id, out var lastRun) && lastRun == thisRunInstance;

                return task.IsEnabled &&
                       !hasRunForThisInstance &&
                       IsScheduledForToday(task, now.DayOfWeek) &&
                       task.ScheduledTime.Hours == now.TimeOfDay.Hours &&
                       task.ScheduledTime.Minutes == now.TimeOfDay.Minutes &&
                       task.ScheduledTime.Seconds == now.TimeOfDay.Seconds;
            }).ToList();

            if (tasksToRun.Any())
            {
                _ = Task.Run(async () =>
                {
                    var firstTask = tasksToRun.First();
                    if (IsMajorOperationInProgress) return;

                    SetOperationLock();
                    try
                    {
                        _lastRunTimestamps[firstTask.Id] = now.Date + firstTask.ScheduledTime;
                        System.Diagnostics.Debug.WriteLine($"Executing scheduled task: {firstTask.Name} of type {firstTask.TaskType}");

                        var activeServers = _appViewModel.Servers.Where(s => s.IsActive).ToList();

                        switch (firstTask.TaskType)
                        {
                            case ScheduledTaskType.DailyReboot:
                                await UpdateManager.PerformScheduledRebootAsync(activeServers, _config);
                                break;
                            case ScheduledTaskType.MaintenanceShutdown:
                                await UpdateManager.PerformMaintenanceShutdownAsync(activeServers, _config);
                                break;
                            case ScheduledTaskType.FrequentBackup:
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