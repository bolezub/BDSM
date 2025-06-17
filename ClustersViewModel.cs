using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;

namespace BDSM
{
    public class ClustersViewModel : BaseViewModel // RENAMED CLASS
    {
        private readonly GlobalConfig _config;
        private ClusterConfig? _selectedCluster;
        private ServerConfig? _selectedServer;
        private bool _structuralChangesMade = false;

        public ObservableCollection<ClusterConfig> Clusters { get; set; }

        public ClusterConfig? SelectedCluster
        {
            get => _selectedCluster;
            set
            {
                _selectedCluster = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsClusterSelected));
                OnPropertyChanged(nameof(SelectedClusterMainModList));
                OnPropertyChanged(nameof(ServersInSelectedCluster));
                SelectedServer = null;
            }
        }

        public ServerConfig? SelectedServer
        {
            get => _selectedServer;
            set
            {
                _selectedServer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsServerSelected));
                OnPropertyChanged(nameof(SelectedServerMapSpecificMods));
                OnPropertyChanged(nameof(IsSelectedServerInstalled));
                OnPropertyChanged(nameof(ShowInstallButton));
            }
        }

        public bool IsClusterSelected => SelectedCluster != null;
        public bool IsServerSelected => SelectedServer != null;

        public bool IsSelectedServerInstalled
        {
            get
            {
                if (SelectedServer == null || string.IsNullOrWhiteSpace(SelectedServer.InstallDir))
                {
                    return false;
                }
                string keyFilePath = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
                return File.Exists(keyFilePath);
            }
        }

        public bool ShowInstallButton => IsServerSelected && !IsSelectedServerInstalled;


        public ObservableCollection<ServerConfig>? ServersInSelectedCluster
        {
            get => SelectedCluster != null ? new ObservableCollection<ServerConfig>(SelectedCluster.Servers) : null;
            set
            {
                if (SelectedCluster != null)
                {
                    SelectedCluster.Servers = value?.ToList() ?? new List<ServerConfig>();
                    OnPropertyChanged();
                }
            }
        }

        #region Proxy Properties for Mod Lists

        public string SelectedClusterMainModList
        {
            get => SelectedCluster != null ? string.Join(",", SelectedCluster.MainModList) : "";
            set
            {
                if (SelectedCluster != null)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        SelectedCluster.MainModList.Clear();
                    }
                    else
                    {
                        SelectedCluster.MainModList = value.Split(',')
                            .Select(part => int.TryParse(part.Trim(), out int modId) ? modId : -1)
                            .Where(modId => modId != -1)
                            .ToList();
                    }
                }
            }
        }

        public string SelectedServerMapSpecificMods
        {
            get => SelectedServer != null ? string.Join(",", SelectedServer.MapSpecificMods) : "";
            set
            {
                if (SelectedServer != null)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        SelectedServer.MapSpecificMods.Clear();
                    }
                    else
                    {
                        SelectedServer.MapSpecificMods = value.Split(',')
                            .Select(part => int.TryParse(part.Trim(), out int modId) ? modId : -1)
                            .Where(modId => modId != -1)
                            .ToList();
                    }
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        public List<string> AvailableMaps => _config.AvailableMaps;

        public ICommand SaveSettingsCommand { get; }
        public ICommand AddClusterCommand { get; }
        public ICommand RemoveClusterCommand { get; }
        public ICommand AddServerCommand { get; }
        public ICommand RemoveServerCommand { get; }
        public ICommand InstallServerCommand { get; }
        public ICommand InstallApiCommand { get; }
        public ICommand LoadFromIniCommand { get; }
        public ICommand SaveToIniCommand { get; }

        public ClustersViewModel(GlobalConfig globalConfig) // RENAMED CONSTRUCTOR
        {
            _config = globalConfig;
            Clusters = new ObservableCollection<ClusterConfig>(_config.Clusters);

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            AddClusterCommand = new RelayCommand(_ => AddCluster());
            RemoveClusterCommand = new RelayCommand(_ => RemoveCluster(), _ => IsClusterSelected);
            AddServerCommand = new RelayCommand(_ => AddServer(), _ => IsClusterSelected);
            RemoveServerCommand = new RelayCommand(_ => RemoveServer(), _ => IsServerSelected);
            InstallServerCommand = new RelayCommand(async _ => await InstallServer(), _ => ShowInstallButton && AreSelectedServerPortsValid() && AreSelectedServerPortsConflictFree());
            InstallApiCommand = new RelayCommand(async _ => await InstallApi(), _ => IsSelectedServerInstalled && !TaskSchedulerService.IsMajorOperationInProgress);
            LoadFromIniCommand = new RelayCommand(async _ => await LoadFromIniAsync(), _ => IsSelectedServerInstalled);
            SaveToIniCommand = new RelayCommand(async _ => await SaveToIniAsync(), _ => IsSelectedServerInstalled);
        }

        private bool AreSelectedServerPortsValid()
        {
            if (SelectedServer == null) return false;
            return SelectedServer.Port > 0 && SelectedServer.QueryPort > 0 && SelectedServer.RconPort > 0;
        }

        private bool AreSelectedServerPortsConflictFree()
        {
            if (SelectedServer == null) return true;

            var otherServers = Clusters.SelectMany(c => c.Servers).Where(s => s != SelectedServer).ToList();

            if (otherServers.Any(s => s.Port == SelectedServer.Port)) return false;
            if (otherServers.Any(s => s.QueryPort == SelectedServer.QueryPort)) return false;
            if (otherServers.Any(s => s.RconPort == SelectedServer.RconPort)) return false;

            return true;
        }

        #region .ini Sync Methods

        private async Task LoadFromIniAsync()
        {
            if (SelectedServer == null) return;

            string iniPath = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
            if (!File.Exists(iniPath))
            {
                MessageBox.Show($"GameUserSettings.ini not found at:\n{iniPath}\n\nRun the server at least once to generate the file.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var iniManager = new IniFileManager(iniPath);
            await iniManager.LoadAsync();

            var portStr = iniManager.GetValue("ServerSettings", "Port");
            if (int.TryParse(portStr, out int port)) SelectedServer.Port = port;

            var queryPortStr = iniManager.GetValue("ServerSettings", "QueryPort");
            if (int.TryParse(queryPortStr, out int queryPort)) SelectedServer.QueryPort = queryPort;

            var rconPortStr = iniManager.GetValue("ServerSettings", "RCONPort");
            if (int.TryParse(rconPortStr, out int rconPort)) SelectedServer.RconPort = rconPort;

            var temp = SelectedServer;
            SelectedServer = null;
            SelectedServer = temp;

            NotificationService.ShowInfo("Settings loaded from GameUserSettings.ini.");
        }

        private async Task SaveToIniAsync()
        {
            if (SelectedServer == null) return;

            string iniPath = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
            var iniManager = new IniFileManager(iniPath);
            await iniManager.LoadAsync();

            iniManager.SetValue("ServerSettings", "Port", SelectedServer.Port.ToString());
            iniManager.SetValue("ServerSettings", "QueryPort", SelectedServer.QueryPort.ToString());
            iniManager.SetValue("ServerSettings", "RCONPort", SelectedServer.RconPort.ToString());
            iniManager.SetValue("ServerSettings", "ServerAdminPassword", _config.RconPassword);

            await iniManager.SaveAsync();
            NotificationService.ShowInfo("Settings saved to GameUserSettings.ini.");
        }

        #endregion

        private async Task InstallApi()
        {
            if (SelectedServer == null) return;
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                MessageBox.Show("Another major operation (like an update or backup) is in progress. Please wait for it to complete.", "Operation in Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var releaseInfo = await ApiManager.GetLatestApiReleaseInfoAsync();
            if (releaseInfo == null)
            {
                MessageBox.Show("Could not fetch the latest API release information from GitHub. Please check your internet connection or try again later.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show($"The latest API version is '{releaseInfo.Version}'.\n\nThis will be installed into:\n{SelectedServer.InstallDir}\n\nContinue?", "Confirm API Installation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No) return;

            TaskSchedulerService.SetOperationLock();
            try
            {
                await ApiManager.DownloadAndInstallApiAsync(releaseInfo, SelectedServer.InstallDir);
                NotificationService.ShowInfo($"Successfully installed API version {releaseInfo.Version}.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"An error occurred during API installation:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private async Task InstallServer()
        {
            if (SelectedServer == null) return;
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                MessageBox.Show("Another major operation (like an update or backup) is in progress. Please wait for it to complete.", "Operation in Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"This will install the server files for '{SelectedServer.Name}' into the directory:\n{SelectedServer.InstallDir}\n\nThis may take a while. Continue?", "Confirm Installation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No) return;

            TaskSchedulerService.SetOperationLock();
            string finalMessage;
            try
            {
                await UpdateManager.InstallServerAsync(SelectedServer, _config);

                finalMessage = "Server installation process has completed.";
                if (!string.IsNullOrWhiteSpace(_config.GameUserSettingsTemplatePath) && File.Exists(_config.GameUserSettingsTemplatePath))
                {
                    System.Diagnostics.Debug.WriteLine("Template file found. Applying template...");
                    string destIniPath = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
                    string destDir = Path.GetDirectoryName(destIniPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);

                    File.Copy(_config.GameUserSettingsTemplatePath, destIniPath, true);

                    var iniManager = new IniFileManager(destIniPath);
                    await iniManager.LoadAsync();
                    iniManager.SetValue("ServerSettings", "Port", SelectedServer.Port.ToString());
                    iniManager.SetValue("ServerSettings", "QueryPort", SelectedServer.QueryPort.ToString());
                    iniManager.SetValue("ServerSettings", "RCONPort", SelectedServer.RconPort.ToString());
                    await iniManager.SaveAsync();

                    finalMessage += " Configuration template was applied successfully.";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Template file not found or path not configured. Skipping template application.");
                }
            }
            catch (System.Exception ex)
            {
                finalMessage = $"An error occurred during installation: {ex.Message}";
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }

            OnPropertyChanged(nameof(IsSelectedServerInstalled));
            OnPropertyChanged(nameof(ShowInstallButton));

            NotificationService.ShowInfo(finalMessage);
        }

        private void AddCluster()
        {
            var newCluster = new ClusterConfig();
            Clusters.Add(newCluster);
            SelectedCluster = newCluster;
            _structuralChangesMade = true;
        }

        private void RemoveCluster()
        {
            if (SelectedCluster == null) return;
            var result = MessageBox.Show($"Are you sure you want to remove the cluster '{SelectedCluster.Name}' and all its servers?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                Clusters.Remove(SelectedCluster);
                _structuralChangesMade = true;
            }
        }

        private void AddServer()
        {
            var newServer = new ServerConfig { Name = "New Server", Active = true, MapFolder = "TheIsland_WP", UseApiLoader = false };
            SelectedCluster?.Servers.Add(newServer);
            OnPropertyChanged(nameof(ServersInSelectedCluster));
            SelectedServer = newServer;
            _structuralChangesMade = true;
        }

        private void RemoveServer()
        {
            if (SelectedCluster == null || SelectedServer == null) return;
            var result = MessageBox.Show($"Are you sure you want to remove the server '{SelectedServer.Name}'?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                SelectedCluster.Servers.Remove(SelectedServer);
                OnPropertyChanged(nameof(ServersInSelectedCluster));
                _structuralChangesMade = true;
            }
        }

        private void SaveSettings()
        {
            var allServers = Clusters.SelectMany(c => c.Servers).ToList();

            var portConflicts = allServers.GroupBy(s => s.Port)
                                          .Where(g => g.Count() > 1)
                                          .Select(g => g.Key.ToString())
                                          .ToList();
            if (portConflicts.Any())
            {
                MessageBox.Show($"Error: Duplicate Game Port found: {string.Join(", ", portConflicts)}. Each server must have a unique Game Port.", "Port Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var queryPortConflicts = allServers.GroupBy(s => s.QueryPort)
                                               .Where(g => g.Count() > 1)
                                               .Select(g => g.Key.ToString())
                                               .ToList();
            if (queryPortConflicts.Any())
            {
                MessageBox.Show($"Error: Duplicate Query Port found: {string.Join(", ", queryPortConflicts)}. Each server must have a unique Query Port.", "Port Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var rconPortConflicts = allServers.GroupBy(s => s.RconPort)
                                              .Where(g => g.Count() > 1)
                                              .Select(g => g.Key.ToString())
                                              .ToList();
            if (rconPortConflicts.Any())
            {
                MessageBox.Show($"Error: Duplicate RCON Port found: {string.Join(", ", rconPortConflicts)}. Each server must have a unique RCON Port.", "Port Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _config.Clusters = Clusters.ToList();
            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_structuralChangesMade)
            {
                MessageBox.Show("Settings saved successfully! You have made structural changes (added/removed a server or cluster). Please restart the application for these changes to be fully reflected on the dashboard.", "Restart Recommended", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                NotificationService.ShowInfo("Settings saved successfully.");
            }

            _structuralChangesMade = false;
        }
    }
}