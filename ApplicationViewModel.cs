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
        public ObservableCollection<ClusterViewModel> Clusters { get; set; }
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
        public StatusBarViewModel StatusBar { get; }

        public object? CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        public ApplicationViewModel()
        {
            StatusBar = new StatusBarViewModel();
            Clusters = new ObservableCollection<ClusterViewModel>();

            ShowDashboardCommand = new RelayCommand(_ => CurrentView = _dashboardView);
            ShowSettingsCommand = new RelayCommand(_ => {
                if (_settingsView == null && _config != null)
                {
                    // This will need to be updated later when we redesign the settings page
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
                    var allServers = Clusters.SelectMany(c => c.Servers);
                    _backupsView = new BackupsView { DataContext = new BackupViewModel(_config, allServers) };
                }
                CurrentView = _backupsView;
            });

            CheckAllServersForUpdateCommand = new RelayCommand(async _ => await CheckAllServersForUpdate(), _ => !TaskSchedulerService.IsMajorOperationInProgress);
            StartUpdateCommand = new RelayCommand(async _ => await StartUpdate(), _ => CanStartUpdate());

            bool isInDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());

            if (!isInDesignMode)
            {
                _config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

                if (_config != null && _config.Clusters != null)
                {
                    // New loading logic for clusters
                    foreach (var clusterConfig in _config.Clusters)
                    {
                        var clusterVM = new ClusterViewModel(clusterConfig, _config);
                        foreach (var serverConfig in clusterConfig.Servers)
                        {
                            var serverVM = new ServerViewModel(serverConfig, clusterConfig, _config);
                            clusterVM.Servers.Add(serverVM);
                        }
                        Clusters.Add(clusterVM);
                    }

                    // Start background services
                    DataLogger.InitializeDatabase(_config.BackupPath);
                    TaskSchedulerService.Start(_config, this);
                    BackupSchedulerService.Start(_config, this);
                    UpdateSchedulerService.Start(_config, this);
                }
            }

            _dashboardView = this;
            CurrentView = _dashboardView;
        }

        public async Task CheckAllServersForUpdate()
        {
            var allServers = Clusters.SelectMany(c => c.Servers);
            var updateTasks = allServers.Select(svm => svm.CheckForUpdate()).ToList();
            await Task.WhenAll(updateTasks);
        }

        private async Task StartUpdate()
        {
            if (_config == null) return;
            var allServers = Clusters.SelectMany(c => c.Servers);
            var serversToUpdate = allServers.Where(s => s.IsUpdateAvailable).ToList();
            if (!serversToUpdate.Any()) return;

            TaskSchedulerService.SetOperationLock();
            try
            {
                await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private bool CanStartUpdate()
        {
            var allServers = Clusters.SelectMany(c => c.Servers);
            return allServers.Any(s => s.IsUpdateAvailable) && !TaskSchedulerService.IsMajorOperationInProgress;
        }
    }
}