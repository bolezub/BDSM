using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading; // Add this using statement

namespace BDSM
{
    public partial class MainWindow : Window
    {
        // The main application timer now lives here, safely in the main window.
        private DispatcherTimer? _mainTimer;

        public MainWindow()
        {
            InitializeComponent();
            NotificationService.RegisterSnackbar(MainSnackbar.MessageQueue);

            // We no longer use the Loaded event. The timer will be started
            // as soon as the DataContext (our ApplicationViewModel) is available.
            this.DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // When the ApplicationViewModel is assigned, start the timer.
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
            // This method is the new "heartbeat" for the entire application.
            if (this.DataContext is not ApplicationViewModel appVM) return;

            try
            {
                // --- Part 1: Update UI Countdown Timers (No changes here) ---
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


                // --- Part 2: Trigger Automated Services (WITH NEW DEBUG LOGGING) ---

                // -- Backup Check --
                bool isBackupTime = DateTime.Now >= BackupSchedulerService.NextBackupTime;
                bool isMajorOpInProgress = TaskSchedulerService.IsMajorOperationInProgress;
                System.Diagnostics.Debug.WriteLine($"--- Backup Check --- Now: {DateTime.Now:T}, Target: {BackupSchedulerService.NextBackupTime:T}. Condition Met: [IsTime={isBackupTime}, IsOpInProgress={isMajorOpInProgress}]");

                if (isBackupTime && !isMajorOpInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("!!! BACKUP TRIGGERED !!!");
                    _ = BackupSchedulerService.RunBackupAndReschedule();
                }

                // -- Update Check --
                bool isUpdateTime = DateTime.Now >= UpdateSchedulerService.NextUpdateCheckTime;
                System.Diagnostics.Debug.WriteLine($"--- Update Check --- Now: {DateTime.Now:T}, Target: {UpdateSchedulerService.NextUpdateCheckTime:T}. Condition Met: [IsTime={isUpdateTime}, IsOpInProgress={isMajorOpInProgress}]");

                if (isUpdateTime && !isMajorOpInProgress)
                {
                    System.Diagnostics.Debug.WriteLine("!!! UPDATE CHECK TRIGGERED !!!");
                    _ = UpdateSchedulerService.RunUpdateCheckAndReschedule();
                }

                // -- Scheduled Task Check --
                System.Diagnostics.Debug.WriteLine("--- Running TaskSchedulerService.CheckAndRunScheduledTasks() ---");
                TaskSchedulerService.CheckAndRunScheduledTasks();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! ERROR in MainTimer_Tick: {ex.Message}");
            }
        }
        // --- Window Control Methods (No changes needed here) ---
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