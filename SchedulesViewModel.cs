using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace BDSM
{
    public class SchedulesViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private ScheduledTask? _selectedTask;
        private readonly DispatcherTimer _countdownTimer;

        private string _editingName = "";
        public string EditingName { get => _editingName; set { _editingName = value; OnPropertyChanged(); } }

        private ScheduledTaskType _editingTaskType;
        public ScheduledTaskType EditingTaskType { get => _editingTaskType; set { _editingTaskType = value; OnPropertyChanged(); } }

        private TimeSpan _editingScheduledTime;
        public TimeSpan EditingScheduledTime { get => _editingScheduledTime; set { _editingScheduledTime = value; OnPropertyChanged(); } }

        private bool _editingIsEnabled;
        public bool EditingIsEnabled { get => _editingIsEnabled; set { _editingIsEnabled = value; OnPropertyChanged(); } }

        private bool _editingRunsOnMonday;
        public bool EditingRunsOnMonday { get => _editingRunsOnMonday; set { _editingRunsOnMonday = value; OnPropertyChanged(); } }

        private bool _editingRunsOnTuesday;
        public bool EditingRunsOnTuesday { get => _editingRunsOnTuesday; set { _editingRunsOnTuesday = value; OnPropertyChanged(); } }

        private bool _editingRunsOnWednesday;
        public bool EditingRunsOnWednesday { get => _editingRunsOnWednesday; set { _editingRunsOnWednesday = value; OnPropertyChanged(); } }

        private bool _editingRunsOnThursday;
        public bool EditingRunsOnThursday { get => _editingRunsOnThursday; set { _editingRunsOnThursday = value; OnPropertyChanged(); } }

        private bool _editingRunsOnFriday;
        public bool EditingRunsOnFriday { get => _editingRunsOnFriday; set { _editingRunsOnFriday = value; OnPropertyChanged(); } }

        private bool _editingRunsOnSaturday;
        public bool EditingRunsOnSaturday { get => _editingRunsOnSaturday; set { _editingRunsOnSaturday = value; OnPropertyChanged(); } }

        private bool _editingRunsOnSunday;
        public bool EditingRunsOnSunday { get => _editingRunsOnSunday; set { _editingRunsOnSunday = value; OnPropertyChanged(); } }

        public ObservableCollection<ScheduledTask> ScheduledTasks { get; set; }

        public ScheduledTask? SelectedTask
        {
            get => _selectedTask;
            set
            {
                _selectedTask = value;
                if (_selectedTask != null)
                {
                    EditingName = _selectedTask.Name;
                    EditingTaskType = _selectedTask.TaskType;
                    EditingScheduledTime = _selectedTask.ScheduledTime;
                    EditingIsEnabled = _selectedTask.IsEnabled;
                    EditingRunsOnMonday = _selectedTask.RunsOnMonday;
                    EditingRunsOnTuesday = _selectedTask.RunsOnTuesday;
                    EditingRunsOnWednesday = _selectedTask.RunsOnWednesday;
                    EditingRunsOnThursday = _selectedTask.RunsOnThursday;
                    EditingRunsOnFriday = _selectedTask.RunsOnFriday;
                    EditingRunsOnSaturday = _selectedTask.RunsOnSaturday;
                    EditingRunsOnSunday = _selectedTask.RunsOnSunday;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTaskSelected));
            }
        }

        public bool IsTaskSelected => SelectedTask != null;

        public IEnumerable<ScheduledTaskType> TaskTypes => Enum.GetValues(typeof(ScheduledTaskType)).Cast<ScheduledTaskType>();

        private string _timeUntilNextRun = "";
        public string TimeUntilNextRun
        {
            get => _timeUntilNextRun;
            set { _timeUntilNextRun = value; OnPropertyChanged(); }
        }

        public ICommand AddTaskCommand { get; }
        public ICommand RemoveTaskCommand { get; }
        public ICommand SaveSchedulesCommand { get; }

        public SchedulesViewModel(GlobalConfig globalConfig)
        {
            _config = globalConfig;
            ScheduledTasks = new ObservableCollection<ScheduledTask>(_config.Schedules);

            AddTaskCommand = new RelayCommand(_ => AddTask());
            RemoveTaskCommand = new RelayCommand(_ => RemoveTask(), _ => IsTaskSelected);
            SaveSchedulesCommand = new RelayCommand(_ => SaveSchedules(), _ => IsTaskSelected);

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            SelectedTask?.Refresh();

            if (SelectedTask?.NextCalculatedRunTime is { } nextRun)
            {
                var timeRemaining = nextRun - DateTime.Now;
                if (timeRemaining.TotalSeconds > 0)
                {
                    // --- IMPROVED FORMATTING LOGIC ---
                    // Now displays days if the countdown is more than 24 hours.
                    if (timeRemaining.TotalDays >= 1)
                    {
                        TimeUntilNextRun = $"(in {timeRemaining:d'd 'hh'h 'mm'm 'ss's'})";
                    }
                    else
                    {
                        TimeUntilNextRun = $"(in {timeRemaining:hh\\:mm\\:ss})";
                    }
                }
                else
                {
                    TimeUntilNextRun = "(Due)";
                }
            }
            else
            {
                TimeUntilNextRun = "(No future run scheduled)";
            }
        }

        private void AddTask()
        {
            var newTask = new ScheduledTask();
            ScheduledTasks.Add(newTask);
            SelectedTask = newTask;
        }

        private void RemoveTask()
        {
            if (SelectedTask == null) return;
            var result = MessageBox.Show($"Are you sure you want to remove the task '{SelectedTask.Name}'?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var taskToRemove = SelectedTask;
                SelectedTask = null;
                ScheduledTasks.Remove(taskToRemove);
                SaveSchedulesToFile();
            }
        }

        private void SaveSchedules()
        {
            if (SelectedTask == null) return;

            SelectedTask.Name = EditingName;
            SelectedTask.TaskType = EditingTaskType;
            SelectedTask.ScheduledTime = EditingScheduledTime;
            SelectedTask.IsEnabled = EditingIsEnabled;
            SelectedTask.RunsOnMonday = EditingRunsOnMonday;
            SelectedTask.RunsOnTuesday = EditingRunsOnTuesday;
            SelectedTask.RunsOnWednesday = EditingRunsOnWednesday;
            SelectedTask.RunsOnThursday = EditingRunsOnThursday;
            SelectedTask.RunsOnFriday = EditingRunsOnFriday;
            SelectedTask.RunsOnSaturday = EditingRunsOnSaturday;
            SelectedTask.RunsOnSunday = EditingRunsOnSunday;

            SaveSchedulesToFile();
        }

        private void SaveSchedulesToFile()
        {
            try
            {
                _config.Schedules = ScheduledTasks.ToList();
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);

                TaskSchedulerService.UpdateNextScheduledTask();

                NotificationService.ShowInfo("Schedules saved successfully!");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save schedules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}