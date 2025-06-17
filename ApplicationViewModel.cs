using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace BDSM
{
    public class ApplicationViewModel : BaseViewModel
    {
        public ObservableCollection<ClusterViewModel> Clusters { get; set; }
        private GlobalConfig? _config;

        private object? _currentView;
        private object? _dashboardView;
        private object? _clustersView;
        private object? _schedulesView;
        private object? _backupsView;
        private object? _globalSettingsView; // NEW View variable

        public ICommand CheckAllServersForUpdateCommand { get; }
        public ICommand StartUpdateCommand { get; }
        public ICommand ShowDashboardCommand { get; }
        public ICommand ShowClustersCommand { get; }
        public ICommand ShowSchedulesCommand { get; }
        public ICommand ShowBackupsCommand { get; }
        public ICommand ShowGlobalSettingsCommand { get; } // NEW Command
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
            ShowClustersCommand = new RelayCommand(_ => {
                if (_clustersView == null && _config != null)
                {
                    _clustersView = new ClustersView { DataContext = new ClustersViewModel(_config) };
                }
                CurrentView = _clustersView;
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
            // NEW Command Initialization
            ShowGlobalSettingsCommand = new RelayCommand(_ => {
                if (_globalSettingsView == null && _config != null)
                {
                    _globalSettingsView = new GlobalSettingsView { DataContext = new GlobalSettingsViewModel(_config) };
                }
                CurrentView = _globalSettingsView;
            });


            CheckAllServersForUpdateCommand = new RelayCommand(async _ => await CheckAllServersForUpdate(), _ => !TaskSchedulerService.IsMajorOperationInProgress);
            StartUpdateCommand = new RelayCommand(async _ => await StartUpdate(), _ => CanStartUpdate());

            bool isInDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());

            if (!isInDesignMode)
            {
                _config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

                if (_config != null)
                {
                    if (_config.AvailableMaps == null || !_config.AvailableMaps.Any())
                    {
                        _config.AvailableMaps = new List<string>
                        {
                            "TheIsland_WP", "ScorchedEarth_WP", "TheCenter_WP", "Aberration_WP", "Extinction_WP", "Astraeos_WP"
                        };
                    }

                    if (_config.Clusters != null)
                    {
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
                    }

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
            var allServers = Clusters.SelectMany(c => c.Servers).Where(s => s.IsInstalled);
            var updateTasks = allServers.Select(svm => svm.CheckForUpdate()).ToList();
            await Task.WhenAll(updateTasks);
        }

        private async Task StartUpdate()
        {
            if (_config == null) return;
            var allServers = Clusters.SelectMany(c => c.Servers);
            var serversToUpdate = allServers.Where(s => s.IsInstalled && s.IsUpdateAvailable).ToList();
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
            return allServers.Any(s => s.IsInstalled && s.IsUpdateAvailable) && !TaskSchedulerService.IsMajorOperationInProgress;
        }
    }
}