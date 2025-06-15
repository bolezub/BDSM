using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private string _timeUntilNextRun = "";

        public ObservableCollection<ScheduledTask> ScheduledTasks { get; set; }

        public ScheduledTask? SelectedTask
        {
            get => _selectedTask;
            set { _selectedTask = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTaskSelected)); }
        }

        public bool IsTaskSelected => SelectedTask != null;

        public IEnumerable<ScheduledTaskType> TaskTypes => Enum.GetValues(typeof(ScheduledTaskType)).Cast<ScheduledTaskType>();

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
            ScheduledTasks.CollectionChanged += OnScheduledTasksChanged;

            AddTaskCommand = new RelayCommand(_ => AddTask());
            RemoveTaskCommand = new RelayCommand(_ => RemoveTask(), _ => IsTaskSelected);
            SaveSchedulesCommand = new RelayCommand(_ => SaveSchedules());

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            // First, force the UI to re-calculate the NextCalculatedRunTime property
            SelectedTask?.Refresh();

            if (SelectedTask?.NextCalculatedRunTime is { } nextRun)
            {
                var timeRemaining = nextRun - DateTime.Now;
                if (timeRemaining.TotalSeconds > 0)
                {
                    TimeUntilNextRun = $"(in {timeRemaining:hh\\:mm\\:ss})";
                }
                else
                {
                    TimeUntilNextRun = "(Now!)";
                }
            }
            else
            {
                TimeUntilNextRun = "";
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
                ScheduledTasks.Remove(SelectedTask);
            }
        }

        private void OnScheduledTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _config.Schedules = ScheduledTasks.ToList();
        }

        private void SaveSchedules()
        {
            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);
                TaskSchedulerService.ClearLastRunHistory();
                TaskSchedulerService.UpdateNextScheduledTask(); // <-- ADD THIS LINE
                MessageBox.Show("Schedules saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save schedules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}