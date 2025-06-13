using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BDSM
{
    public enum ScheduledTaskType
    {
        DailyReboot,
        MaintenanceShutdown
    }

    public class ScheduledTask : BaseViewModel
    {
        // Private fields for properties
        private string _name = "New Task";
        private ScheduledTaskType _taskType = ScheduledTaskType.DailyReboot;
        private TimeSpan _scheduledTime = new TimeSpan(5, 0, 0);
        private bool _isEnabled = true;
        private bool _runsOnMonday = true;
        private bool _runsOnTuesday = true;
        private bool _runsOnWednesday = true;
        private bool _runsOnThursday = true;
        private bool _runsOnFriday = true;
        private bool _runsOnSaturday = true;
        private bool _runsOnSunday = true;

        // Public properties that notify the UI on change
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        [JsonConverter(typeof(StringEnumConverter))]
        public ScheduledTaskType TaskType { get => _taskType; set { _taskType = value; OnPropertyChanged(); } }
        public TimeSpan ScheduledTime { get => _scheduledTime; set { _scheduledTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnMonday { get => _runsOnMonday; set { _runsOnMonday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnTuesday { get => _runsOnTuesday; set { _runsOnTuesday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnWednesday { get => _runsOnWednesday; set { _runsOnWednesday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnThursday { get => _runsOnThursday; set { _runsOnThursday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnFriday { get => _runsOnFriday; set { _runsOnFriday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnSaturday { get => _runsOnSaturday; set { _runsOnSaturday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }
        public bool RunsOnSunday { get => _runsOnSunday; set { _runsOnSunday = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextCalculatedRunTime)); } }

        [JsonIgnore]
        public DateTime? NextCalculatedRunTime
        {
            get
            {
                if (!IsEnabled) return null;

                var now = DateTime.Now;
                for (int i = 0; i < 7; i++)
                {
                    var checkDay = DateTime.Today.AddDays(i);
                    bool shouldRunOnThisDay = false;
                    switch (checkDay.DayOfWeek)
                    {
                        case DayOfWeek.Monday: shouldRunOnThisDay = RunsOnMonday; break;
                        case DayOfWeek.Tuesday: shouldRunOnThisDay = RunsOnTuesday; break;
                        case DayOfWeek.Wednesday: shouldRunOnThisDay = RunsOnWednesday; break;
                        case DayOfWeek.Thursday: shouldRunOnThisDay = RunsOnThursday; break;
                        case DayOfWeek.Friday: shouldRunOnThisDay = RunsOnFriday; break;
                        case DayOfWeek.Saturday: shouldRunOnThisDay = RunsOnSaturday; break;
                        case DayOfWeek.Sunday: shouldRunOnThisDay = RunsOnSunday; break;
                    }

                    if (shouldRunOnThisDay)
                    {
                        var nextRun = checkDay.Date + ScheduledTime;
                        // If the calculated time for today is already past, we look for the next valid day
                        if (nextRun > now)
                        {
                            return nextRun;
                        }
                    }
                }
                // If no time in the next 7 days is found, it means the next instance is more than a week away
                // (This can happen if all days are unchecked, so we return null)
                return null;
            }
        }

        // This method will be called by our UI timer to force the NextCalculatedRunTime to be re-evaluated
        public void Refresh()
        {
            OnPropertyChanged(nameof(NextCalculatedRunTime));
        }
    }
}