using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BDSM
{
    public static class TaskSchedulerService
    {
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;
        private static readonly Dictionary<Guid, DateTime> _lastRunTimestamps = new Dictionary<Guid, DateTime>();

        public static ScheduledTask? NextScheduledTask { get; private set; }

        public static bool IsMajorOperationInProgress { get; private set; } = false;

        public static void SetOperationLock()
        {
            IsMajorOperationInProgress = true;
            CommandManager.InvalidateRequerySuggested();
        }

        public static void ReleaseOperationLock()
        {
            IsMajorOperationInProgress = false;
            CommandManager.InvalidateRequerySuggested();
        }

        public static void ClearLastRunHistory()
        {
            _lastRunTimestamps.Clear();
        }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            UpdateNextScheduledTask();
        }

        public static void PreventMissedTasksOnStartup()
        {
            if (_config == null) return;

            var now = DateTime.Now;
            var today = now.Date;

            var pastTasksForToday = _config.Schedules.Where(s =>
            {
                if (!s.IsEnabled) return false;

                bool runsToday = now.DayOfWeek switch
                {
                    DayOfWeek.Monday => s.RunsOnMonday,
                    DayOfWeek.Tuesday => s.RunsOnTuesday,
                    DayOfWeek.Wednesday => s.RunsOnWednesday,
                    DayOfWeek.Thursday => s.RunsOnThursday,
                    DayOfWeek.Friday => s.RunsOnFriday,
                    DayOfWeek.Saturday => s.RunsOnSaturday,
                    DayOfWeek.Sunday => s.RunsOnSunday,
                    _ => false
                };

                if (!runsToday) return false;

                return today + s.ScheduledTime < now;
            });

            foreach (var task in pastTasksForToday)
            {
                _lastRunTimestamps[task.Id] = today + task.ScheduledTime;
            }
        }

        public static void UpdateNextScheduledTask()
        {
            if (_config == null) return;
            NextScheduledTask = _config.Schedules
                .Where(s => s.IsEnabled && s.NextCalculatedRunTime.HasValue)
                .OrderBy(s => s.NextCalculatedRunTime)
                .FirstOrDefault();
        }

        public static void CheckAndRunScheduledTasks()
        {
            if (_config == null || _appViewModel == null || IsMajorOperationInProgress)
            {
                return;
            }

            var now = DateTime.Now;

            var dueTasks = _config.Schedules.Where(s =>
            {
                if (!s.IsEnabled) return false;

                bool runsOnThisDay = DateTime.Today.DayOfWeek switch
                {
                    DayOfWeek.Monday => s.RunsOnMonday,
                    DayOfWeek.Tuesday => s.RunsOnTuesday,
                    DayOfWeek.Wednesday => s.RunsOnWednesday,
                    DayOfWeek.Thursday => s.RunsOnThursday,
                    DayOfWeek.Friday => s.RunsOnFriday,
                    DayOfWeek.Saturday => s.RunsOnSaturday,
                    DayOfWeek.Sunday => s.RunsOnSunday,
                    _ => false
                };

                if (!runsOnThisDay) return false;

                var todayRuntime = DateTime.Today + s.ScheduledTime;
                return now >= todayRuntime;

            }).ToList();

            if (!dueTasks.Any()) return;

            foreach (var taskToRun in dueTasks)
            {
                var thisRunInstance = now.Date + taskToRun.ScheduledTime;
                if (_lastRunTimestamps.ContainsKey(taskToRun.Id) && _lastRunTimestamps[taskToRun.Id] >= thisRunInstance)
                {
                    continue;
                }

                _lastRunTimestamps[taskToRun.Id] = thisRunInstance;

                _ = Task.Run(async () =>
                {
                    var activeServers = _appViewModel.Clusters.SelectMany(c => c.Servers).Where(s => s.IsActive && s.IsInstalled).ToList();
                    switch (taskToRun.TaskType)
                    {
                        case ScheduledTaskType.DailyReboot:
                            await UpdateManager.PerformSimpleRebootAsync(activeServers, _config);
                            break;
                        case ScheduledTaskType.MaintenanceShutdown:
                            await UpdateManager.PerformMaintenanceShutdownAsync(activeServers, _config);
                            break;
                        case ScheduledTaskType.ScheduledBackup:
                            await BackupManager.PerformBackupAsync(activeServers, _config);
                            break;
                    }
                });
            }
        }
    }
}