using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            Debug.WriteLine("Scheduler history cleared on startup.");
        }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;
            UpdateNextScheduledTask();
        }

        // --- NEW METHOD ---
        /// <summary>
        /// Prevents the scheduler from running tasks that were missed while the application was closed.
        /// It does this by pre-populating the run history for any tasks scheduled for earlier today.
        /// </summary>
        public static void PreventMissedTasksOnStartup()
        {
            if (_config == null) return;

            var now = DateTime.Now;
            var today = now.Date;

            // Find all tasks scheduled for today whose time has already passed.
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

                // Check if the scheduled time for today is in the past
                return today + s.ScheduledTime < now;
            });

            // For each of these tasks, add a record to the history as if it has already run.
            foreach (var task in pastTasksForToday)
            {
                _lastRunTimestamps[task.Id] = today + task.ScheduledTime;
                Debug.WriteLine($"[Startup] Pre-emptively marking past task '{task.Name}' (scheduled for {task.ScheduledTime}) as complete for today to prevent re-run.");
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
                Debug.WriteLine($"!!! TASK TRIGGERED: '{taskToRun.Name}'. Setting flag and executing. !!!");

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