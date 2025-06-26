using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BDSM
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer? _mainTimer;

        public MainWindow()
        {
            InitializeComponent();
            NotificationService.RegisterSnackbar(MainSnackbar.MessageQueue);

            this.DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ApplicationViewModel)
            {
                StartMainTimer();
            }
        }

        private void StartMainTimer()
        {
            if (_mainTimer != null && _mainTimer.IsEnabled) return;

            _mainTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mainTimer.Tick += MainTimer_Tick;
            _mainTimer.Start();
        }

        private void MainTimer_Tick(object? sender, EventArgs e)
        {
            if (this.DataContext is not ApplicationViewModel appVM) return;

            try
            {
                var statusBar = appVM.StatusBar;

                var backupTimeRemaining = BackupSchedulerService.NextBackupTime - DateTime.Now;
                statusBar.NextBackupText = backupTimeRemaining.TotalSeconds > 0 ? $"Next Backup: {backupTimeRemaining:hh\\:mm\\:ss}" : "Next Backup: Due...";

                var updateTimeRemaining = UpdateSchedulerService.NextUpdateCheckTime - DateTime.Now;
                statusBar.NextUpdateText = updateTimeRemaining.TotalSeconds > 0 ? $"Next Update Check: {updateTimeRemaining:hh\\:mm\\:ss}" : "Next Update Check: Due...";

                if (TaskSchedulerService.NextScheduledTask?.NextCalculatedRunTime is { } nextRun)
                {
                    var taskTimeRemaining = nextRun - DateTime.Now;
                    statusBar.NextScheduledTaskText = taskTimeRemaining.TotalSeconds > 0 ? $"Next Task ({TaskSchedulerService.NextScheduledTask.Name}): {taskTimeRemaining:hh\\:mm\\:ss}" : $"Next Task ({TaskSchedulerService.NextScheduledTask.Name}): Due...";
                }
                else
                {
                    statusBar.NextScheduledTaskText = "No further tasks scheduled.";
                }

                if (DateTime.Now >= BackupSchedulerService.NextBackupTime && !TaskSchedulerService.IsMajorOperationInProgress)
                {
                    _ = BackupSchedulerService.RunBackupAndReschedule();
                }

                if (DateTime.Now >= UpdateSchedulerService.NextUpdateCheckTime && !TaskSchedulerService.IsMajorOperationInProgress)
                {
                    _ = UpdateSchedulerService.RunUpdateCheckAndReschedule();
                }

                TaskSchedulerService.CheckAndRunScheduledTasks();
            }
            catch (Exception)
            {
                // Error logging removed
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
    }
}