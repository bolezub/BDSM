using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace BDSM
{
    public class ApplicationViewModel : BaseViewModel
    {
        public ObservableCollection<ClusterViewModel> Clusters { get; }
        private GlobalConfig? _config;
        private IServiceProvider? _services;

        public Task InitializationTask { get; private set; }

        private object? _currentView;
        private object? _dashboardView;
        private object? _clustersView;
        private object? _schedulesView;
        private object? _backupsView;
        private object? _globalSettingsView;
        private object? _watchdogView;

        public ICommand CheckAllServersForUpdateCommand { get; }
        public ICommand StartUpdateCommand { get; }
        public ICommand ShowDashboardCommand { get; }
        public ICommand ShowClustersCommand { get; }
        public ICommand ShowSchedulesCommand { get; }
        public ICommand ShowBackupsCommand { get; }
        public ICommand ShowGlobalSettingsCommand { get; }
        public ICommand ShowWatchdogCommand { get; }
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
                if (_clustersView == null && _config != null) { _clustersView = new ClustersView { DataContext = new ClustersViewModel(_config, this) }; }
                CurrentView = _clustersView;
            });
            ShowSchedulesCommand = new RelayCommand(_ => {
                if (_schedulesView == null && _config != null) { _schedulesView = new SchedulesView { DataContext = new SchedulesViewModel(_config) }; }
                CurrentView = _schedulesView;
            });
            ShowBackupsCommand = new RelayCommand(_ => {
                if (_backupsView == null && _config != null) { _backupsView = new BackupsView { DataContext = new BackupViewModel(_config, Clusters.SelectMany(c => c.Servers)) }; }
                CurrentView = _backupsView;
            });
            ShowGlobalSettingsCommand = new RelayCommand(_ => {
                if (_globalSettingsView == null && _config != null) { _globalSettingsView = new GlobalSettingsView { DataContext = new GlobalSettingsViewModel(_config) }; }
                CurrentView = _globalSettingsView;
            });
            ShowWatchdogCommand = new RelayCommand(_ => {
                if (_watchdogView == null && _config != null) { _watchdogView = new WatchdogView { DataContext = new WatchdogViewModel(_config) }; }
                CurrentView = _watchdogView;
            });

            CheckAllServersForUpdateCommand = new RelayCommand(async _ => await CheckAllServersForUpdate(), _ => !TaskSchedulerService.IsMajorOperationInProgress);
            StartUpdateCommand = new RelayCommand(async _ => await StartUpdate(), _ => CanStartUpdate());

            InitializationTask = InitializeApplication();
        }

        private async Task InitializeApplication()
        {
            bool isInDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());
            if (isInDesignMode) return;

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(this);
            _services = serviceCollection.BuildServiceProvider();

            _config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

            if (_config != null)
            {
                bool configWasModified = false;
                if (_config.Clusters != null)
                {
                    foreach (var server in _config.Clusters.SelectMany(c => c.Servers)) { if (server.Id == Guid.Empty) { server.Id = Guid.NewGuid(); configWasModified = true; } }
                }
                if (configWasModified) { File.WriteAllText("config.json", JsonConvert.SerializeObject(_config, Formatting.Indented)); }
                if (_config.AvailableMaps == null || !_config.AvailableMaps.Any()) { _config.AvailableMaps = new List<string> { "TheIsland_WP", "ScorchedEarth_WP", "TheCenter_WP", "Aberration_WP", "Extinction_WP", "Astraeos_WP" }; }

                if (_config.Clusters != null)
                {
                    var clusterTasks = _config.Clusters.Select(async clusterConfig =>
                    {
                        var clusterVM = new ClusterViewModel(clusterConfig, _config, this);
                        var serverTasks = clusterConfig.Servers.Select(serverConfig => ServerViewModel.CreateAsync(serverConfig, clusterConfig, _config));
                        var serverVMs = await Task.WhenAll(serverTasks);
                        foreach (var serverVM in serverVMs) { clusterVM.Servers.Add(serverVM); }
                        return clusterVM;
                    });
                    var createdClusters = await Task.WhenAll(clusterTasks);
                    foreach (var cluster in createdClusters) { Clusters.Add(cluster); }
                }

                DataLogger.InitializeDatabase(_config.BackupPath);
                TaskSchedulerService.Start(_config, this);
                BackupSchedulerService.Start(_config, this);
                UpdateSchedulerService.Start(_config, this);
                await WatchdogService.InitializeAndStart(_config, this);
                await DiscordBotService.StartAsync(_config, _services);
            }

            _dashboardView = this;
            CurrentView = _dashboardView;
        }

        public async Task CheckAllServersForUpdate()
        {
            var allServers = Clusters.SelectMany(c => c.Servers).Where(s => s.IsInstalled);
            await Task.WhenAll(allServers.Select(svm => svm.CheckForUpdate()).ToList());
        }

        private async Task StartUpdate()
        {
            if (_config == null) return;
            var serversToUpdate = Clusters.SelectMany(c => c.Servers).Where(s => s.IsInstalled && s.IsUpdateAvailable).ToList();
            if (!serversToUpdate.Any()) return;
            TaskSchedulerService.SetOperationLock();
            try { await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config); }
            finally { TaskSchedulerService.ReleaseOperationLock(); }
        }

        private bool CanStartUpdate() => Clusters.SelectMany(c => c.Servers).Any(s => s.IsInstalled && s.IsUpdateAvailable);
    }
}