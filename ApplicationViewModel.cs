using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;

namespace BDSM
{
    public class ApplicationViewModel : BaseViewModel
    {
        public ObservableCollection<ServerViewModel> Servers { get; set; }
        private GlobalConfig? _config;

        private object? _currentView;
        private object? _dashboardView;
        private object? _settingsView;
        private object? _schedulesView;
        private object? _backupsView;

        public ICommand CheckAllServersForUpdateCommand { get; }
        public ICommand StartUpdateCommand { get; }
        public ICommand ShowDashboardCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand ShowSchedulesCommand { get; }
        public ICommand ShowBackupsCommand { get; }

        public object? CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        public ApplicationViewModel()
        {
            Servers = new ObservableCollection<ServerViewModel>();

            ShowDashboardCommand = new RelayCommand(_ => CurrentView = _dashboardView);
            ShowSettingsCommand = new RelayCommand(_ => {
                if (_settingsView == null && _config != null)
                {
                    _settingsView = new SettingsView { DataContext = new SettingsViewModel(_config) };
                }
                CurrentView = _settingsView;
            });
            ShowSchedulesCommand = new RelayCommand(_ => {
                if (_schedulesView == null && _config != null)
                {
                    _schedulesView = new SchedulesView { DataContext = new SchedulesViewModel(_config) };
                }
                CurrentView = _schedulesView;
            });

            ShowBackupsCommand = new RelayCommand(_ => {
                if (_backupsView == null && _config != null)
                {
                    _backupsView = new BackupsView { DataContext = new BackupViewModel(_config, Servers) };
                }
                CurrentView = _backupsView;
            });

            CheckAllServersForUpdateCommand = new RelayCommand(async _ => await CheckAllServersForUpdate(), _ => !TaskSchedulerService.IsMajorOperationInProgress);
            StartUpdateCommand = new RelayCommand(async _ => await StartUpdate(), _ => CanStartUpdate());

            bool isInDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());

            if (!isInDesignMode)
            {
                _config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

                if (_config != null && _config.Servers != null)
                {
                    DataLogger.InitializeDatabase(_config.BackupPath);
                    TaskSchedulerService.Start(_config, this);
                    BackupSchedulerService.Start(_config, this); // <-- ADD THIS LINE

                    foreach (var serverConfig in _config.Servers)
                    {
                        var svm = new ServerViewModel(serverConfig, _config);
                        Servers.Add(svm);
                    }
                }
            }

            _dashboardView = this;
            CurrentView = _dashboardView;
        }

        private void ShowNotification(string message, int durationSeconds = 5)
        {
            System.Diagnostics.Debug.WriteLine($"NOTIFICATION: {message}");
        }

        public async Task CheckAllServersForUpdate()
        {
            ShowNotification("Checking all servers for updates...", 2);
            var updateTasks = Servers.Select(svm => svm.CheckForUpdate()).ToList();
            await Task.WhenAll(updateTasks);
            ShowNotification("Update check finished.");
        }

        private async Task StartUpdate()
        {
            if (_config == null) return;

            var serversToUpdate = Servers.Where(s => s.IsUpdateAvailable).ToList();

            if (!serversToUpdate.Any())
            {
                ShowNotification("No servers require an update.");
                return;
            }

            TaskSchedulerService.SetOperationLock();
            try
            {
                ShowNotification($"Starting update process for {serversToUpdate.Count} server(s)...", 10);
                await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
                ShowNotification("Update process complete for all servers.");
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private bool CanStartUpdate()
        {
            return Servers.Any(s => s.IsUpdateAvailable) && !TaskSchedulerService.IsMajorOperationInProgress;
        }
    }
}