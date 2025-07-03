using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BDSM
{
    public class ApplicationViewModel : BaseViewModel
    {
        public ObservableCollection<ClusterViewModel> Clusters { get; }
        private GlobalConfig? _config;
        private IServiceProvider? _services;

        public Task<bool> InitializationTask { get; private set; }

        private object? _currentView;
        private object? _dashboardView;
        private object? _clustersView;
        private object? _schedulesView;
        private object? _backupsView;
        private object? _globalSettingsView;
        private object? _watchdogView;
        private object? _eventLogView;

        public ICommand StartUpdateCommand { get; }
        public ICommand ShowDashboardCommand { get; }
        public ICommand ShowClustersCommand { get; }
        public ICommand ShowSchedulesCommand { get; }
        public ICommand ShowBackupsCommand { get; }
        public ICommand ShowGlobalSettingsCommand { get; }
        public ICommand ShowWatchdogCommand { get; }
        public ICommand ShowEventLogCommand { get; }
        public StatusBarViewModel StatusBar { get; }

        public ICommand ManualUpdateCheckCommand { get; }

        private bool _hasNewErrorLogs;
        public bool HasNewErrorLogs
        {
            get => _hasNewErrorLogs;
            private set { _hasNewErrorLogs = value; OnPropertyChanged(); }
        }

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
            ShowEventLogCommand = new RelayCommand(_ => {
                if (_eventLogView == null) { _eventLogView = new EventLogView { DataContext = new EventLogViewModel() }; }
                else { ((_eventLogView as EventLogView).DataContext as EventLogViewModel).RefreshLogCommand.Execute(null); }
                CurrentView = _eventLogView;
                HasNewErrorLogs = false;
            });

            StartUpdateCommand = new RelayCommand(async _ => await StartUpdate(), _ => Clusters.SelectMany(c => c.Servers).Any(s => s.IsInstalled && s.IsUpdateAvailable));

            ManualUpdateCheckCommand = new RelayCommand(async _ => await RunManualUpdateCheck(), _ => !TaskSchedulerService.IsMajorOperationInProgress);

            LoggingService.OnNewLogEntry += OnNewLogEntry;

            InitializationTask = InitializeApplication();
        }

        private async Task RunManualUpdateCheck()
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                NotificationService.ShowInfo("Cannot check for updates while another operation is in progress.");
                return;
            }

            NotificationService.ShowInfo("Manually checking all servers for updates...");
            await CheckAllServersForUpdate();
            UpdateSchedulerService.RecalculateNextRunTime();
            NotificationService.ShowInfo("Update check complete.");
        }

        private void OnNewLogEntry(LogLevel level)
        {
            if (level == LogLevel.Error || level == LogLevel.Warning)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    HasNewErrorLogs = true;
                });
            }
        }

        private async Task CleanWatchdogChannelAsync()
        {
            if (_config == null || string.IsNullOrWhiteSpace(_config.WatchdogDiscordWebhookUrl) || string.IsNullOrWhiteSpace(_config.BotToken))
            {
                return;
            }

            string channelId = string.Empty;
            List<DiscordMessage> messages;

            try
            {
                using (var httpClient = new HttpClient())
                {
                    var webhookInfo = await httpClient.GetFromJsonAsync<WebhookInfoResponse>(_config.WatchdogDiscordWebhookUrl);
                    if (webhookInfo == null || string.IsNullOrWhiteSpace(webhookInfo.ChannelId)) return;
                    channelId = webhookInfo.ChannelId;
                }

                using (var authClient = new HttpClient())
                {
                    authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _config.BotToken);
                    messages = await authClient.GetFromJsonAsync<List<DiscordMessage>>($"https://discord.com/api/v9/channels/{channelId}/messages?limit=100");
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Failed to fetch messages for initial cleanup: {ex.Message}", LogLevel.Warning);
                return;
            }

            if (messages == null || !messages.Any())
            {
                return;
            }

            try
            {
                using (var authClient = new HttpClient())
                {
                    authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _config.BotToken);
                    foreach (var message in messages)
                    {
                        var deleteUrl = $"https://discord.com/api/v9/channels/{channelId}/messages/{message.Id}";
                        await authClient.DeleteAsync(deleteUrl);
                        await Task.Delay(1100);
                    }
                }
                LoggingService.Log($"Successfully cleaned {messages.Count} old messages from the watchdog channel.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"An error occurred during watchdog channel cleanup: {ex.Message}", LogLevel.Warning);
            }
        }

        private async Task<bool> InitializeApplication()
        {
            bool isFirstRun = false;
            bool isInDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());
            if (isInDesignMode) return false;

            if (File.Exists("config.json"))
            {
                _config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));
            }
            else
            {
                isFirstRun = true;
                _config = new GlobalConfig
                {
                    Clusters = new List<ClusterConfig>(),
                    Schedules = new List<ScheduledTask>(),
                    BackupIntervalMinutes = 30,
                    UpdateCheckIntervalMinutes = 30,
                    ShutdownTimeoutSeconds = 120,
                    ServerIP = "127.0.0.1",
                    AvailableMaps = new List<string> { "TheIsland_WP", "ScorchedEarth_WP", "TheCenter_WP", "Aberration_WP", "Extinction_WP" },
                    StartArgumentsTemplate = "{mapFolder}?listen?MultiHome={serverIP}?Port={port}?QueryPort={queryPort}?AllowCrateSpawnsOnTopOfStructures=True -noundermeshchecking -noundermeshkilling -EnableIdlePlayerKick -clusterid={clusterId} -ClusterDirOverride=d:\\asadata -NoTransferFromFiltering -forcerespawndinos -servergamelog -servergamelogincludetribelogs -ServerRCONOutputTribeLogs -nobattleye -WinLiveMaxPlayers=60",
                    AppId = "2430930",
                    SteamApiUrl = "https://api.steamcmd.net/v1/info/2430930"
                };
                string defaultConfigJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", defaultConfigJson);
            }

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(this);
            _services = serviceCollection.BuildServiceProvider();

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

                var allServers = Clusters.SelectMany(c => c.Servers).Where(s => s.IsInstalled).ToList();

                var statusCheckTasks = allServers.Select(server => server.UpdateServerStatus()).ToList();
                await Task.WhenAll(statusCheckTasks);

                var versionCheckTasks = allServers.Select(server => server.CheckForUpdate()).ToList();
                await Task.WhenAll(versionCheckTasks);

                DataLogger.InitializeDatabase(_config.BackupPath);
                TaskSchedulerService.ClearLastRunHistory();
                TaskSchedulerService.Start(_config, this);
                TaskSchedulerService.PreventMissedTasksOnStartup();
                BackupSchedulerService.Start(_config, this);
                UpdateSchedulerService.Start(_config, this);

                _ = CleanWatchdogChannelAsync();

                _ = WatchdogService.InitializeAndStart(_config, this);
                _ = DiscordBotService.StartAsync(_config, _services);

                // Make the initial check non-blocking
                _ = CheckAllServersForUpdate();
            }

            _dashboardView = this;
            CurrentView = _dashboardView;

            return isFirstRun;
        }

        // --- METHOD REFACTORED TO BE MORE EFFICIENT ---
        public async Task CheckAllServersForUpdate()
        {
            if (_config == null) return;

            // Step 1: Fetch the latest build ID only ONCE.
            string? latestBuild = await UpdateManager.GetLatestBuildIdAsync(_config.SteamApiUrl, _config.AppId);

            // Step 2: Update the status bar immediately with the result.
            if (!string.IsNullOrEmpty(latestBuild) && latestBuild != "0")
            {
                StatusBar.LatestBuildText = $"Latest Build: {latestBuild}";
            }
            else
            {
                StatusBar.LatestBuildText = "Latest Build: API Error";
            }

            // Step 3: Pass the fetched build ID to each server for a quick comparison.
            var allServers = Clusters.SelectMany(c => c.Servers).Where(s => s.IsInstalled);
            await Task.WhenAll(allServers.Select(svm => svm.CheckForUpdate(latestBuild)).ToList());

            // Step 4: Force the UI to re-evaluate all command states.
            Application.Current.Dispatcher.Invoke(CommandManager.InvalidateRequerySuggested);
        }


        private async Task StartUpdate()
        {
            if (_config == null) return;
            var serversToUpdate = Clusters.SelectMany(c => c.Servers).Where(s => s.IsInstalled && s.IsUpdateAvailable).ToList();
            if (!serversToUpdate.Any()) return;

            await UpdateManager.PerformUpdateProcessAsync(serversToUpdate, _config);
        }

        private bool CanStartUpdate() => Clusters.SelectMany(c => c.Servers).Any(s => s.IsInstalled && s.IsUpdateAvailable);

        // This method is now redundant and has been removed. Its logic is inside CheckAllServersForUpdate.
        // public async Task UpdateLatestBuildIdAsync() { ... }

        private record WebhookInfoResponse([property: JsonPropertyName("channel_id")] string ChannelId);
    }
}