using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BDSM
{
    public enum ScheduledTaskType
    {
        DailyReboot,
        MaintenanceShutdown,
        ScheduledBackup
    }

    public class ScheduledTask : BaseViewModel
    {
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

                // Search for the next 14 days to be absolutely sure we find the next valid run time.
                for (int i = 0; i < 14; i++)
                {
                    var checkDate = now.Date.AddDays(i);
                    if (IsScheduledForDay(checkDate.DayOfWeek))
                    {
                        var potentialRunTime = checkDate.Date + ScheduledTime;

                        // If the potential time is in the future, it's a valid candidate.
                        // The first one we find will be the soonest.
                        if (potentialRunTime > now)
                        {
                            return potentialRunTime;
                        }
                    }
                }

                // If no days are checked at all, or no future time is found, return null.
                return null;
            }
        }

        private bool IsScheduledForDay(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => RunsOnMonday,
                DayOfWeek.Tuesday => RunsOnTuesday,
                DayOfWeek.Wednesday => RunsOnWednesday,
                DayOfWeek.Thursday => RunsOnThursday,
                DayOfWeek.Friday => RunsOnFriday,
                DayOfWeek.Saturday => RunsOnSaturday,
                DayOfWeek.Sunday => RunsOnSunday,
                _ => false,
            };
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(NextCalculatedRunTime));
        }
    }
}