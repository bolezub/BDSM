using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BDSM
{
    public class ClustersViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private readonly ApplicationViewModel _appViewModel;
        private ClusterConfig? _selectedCluster;
        private ServerConfig? _selectedServer;
        private ServerViewModel? _liveSelectedServer;
        private bool _structuralChangesMade = false;

        public ObservableCollection<ClusterConfig> Clusters { get; set; }
        public ICommand VerifyServerFilesCommand { get; }

        public ClusterConfig? SelectedCluster
        {
            get => _selectedCluster;
            set
            {
                _selectedCluster = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsClusterSelected));
                OnPropertyChanged(nameof(SelectedClusterMainModList));
                UpdateServersInSelectedCluster();
                SelectedServer = null;
            }
        }

        public ServerConfig? SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (_liveSelectedServer != null)
                {
                    _liveSelectedServer.PropertyChanged -= OnLiveServerPropertyChanged;
                }

                _selectedServer = value;

                if (_selectedServer != null)
                {
                    _liveSelectedServer = _appViewModel.Clusters
                        .SelectMany(c => c.Servers)
                        .FirstOrDefault(svm => svm.ServerId == _selectedServer.Id);

                    if (_liveSelectedServer != null)
                    {
                        _liveSelectedServer.PropertyChanged += OnLiveServerPropertyChanged;
                    }
                }
                else
                {
                    _liveSelectedServer = null;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsServerSelected));
                OnPropertyChanged(nameof(SelectedServerMapSpecificMods));
                OnPropertyChanged(nameof(IsSelectedServerInstalled));
                OnPropertyChanged(nameof(ShowInstallButton));
                OnPropertyChanged(nameof(IsSelectedServerEditable));
            }
        }

        private void OnLiveServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServerViewModel.Status))
            {
                OnPropertyChanged(nameof(IsSelectedServerEditable));
            }
        }

        public bool IsClusterSelected => SelectedCluster != null;
        public bool IsServerSelected => SelectedServer != null;

        public bool IsSelectedServerEditable
        {
            get
            {
                if (_liveSelectedServer == null)
                {
                    return IsServerSelected;
                }
                return _liveSelectedServer.Status == "Stopped" ||
                       _liveSelectedServer.Status == "Not Installed" ||
                       _liveSelectedServer.Status == "Unknown";
            }
        }

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

        public ObservableCollection<ServerConfig> ServersInSelectedCluster { get; set; }

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
        public ICommand MoveServerUpCommand { get; }
        public ICommand MoveServerDownCommand { get; }
        public ICommand BrowseInstallDirCommand { get; }
        public ICommand OpenRootFolderCommand { get; }
        public ICommand OpenIniFolderCommand { get; }
        public ICommand OpenInstallFolderCommand { get; }
        public ICommand OpenMapSaveFolderCommand { get; }
        // --- NEW COMMAND ---
        public ICommand ImportServerCommand { get; }


        public ClustersViewModel(GlobalConfig globalConfig, ApplicationViewModel appViewModel)
        {
            _config = globalConfig;
            _appViewModel = appViewModel;
            Clusters = new ObservableCollection<ClusterConfig>(_config.Clusters);
            ServersInSelectedCluster = new ObservableCollection<ServerConfig>();

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            AddClusterCommand = new RelayCommand(_ => AddCluster());
            RemoveClusterCommand = new RelayCommand(_ => RemoveCluster(), _ => IsClusterSelected);
            AddServerCommand = new RelayCommand(_ => AddServer(), _ => IsClusterSelected);
            RemoveServerCommand = new RelayCommand(_ => RemoveServer(), _ => IsServerSelected);
            InstallServerCommand = new RelayCommand(async _ => await InstallServer(), _ => ShowInstallButton && AreSelectedServerPortsValid() && AreSelectedServerPortsConflictFree());
            InstallApiCommand = new RelayCommand(async _ => await InstallApi(), _ => IsSelectedServerInstalled && !TaskSchedulerService.IsMajorOperationInProgress);
            LoadFromIniCommand = new RelayCommand(async _ => await LoadFromIniAsync(), _ => IsSelectedServerInstalled);
            SaveToIniCommand = new RelayCommand(async _ => await SaveToIniAsync(), _ => IsSelectedServerInstalled);
            MoveServerUpCommand = new RelayCommand(_ => MoveServer(-1), _ => SelectedServer != null && ServersInSelectedCluster.IndexOf(SelectedServer) > 0);
            MoveServerDownCommand = new RelayCommand(_ => MoveServer(1), _ => SelectedServer != null && ServersInSelectedCluster.IndexOf(SelectedServer) < ServersInSelectedCluster.Count - 1);
            BrowseInstallDirCommand = new RelayCommand(_ => BrowseForFolder(SelectedServer?.InstallDir ?? "", path => { if (SelectedServer != null) SelectedServer.InstallDir = path; OnPropertyChanged(nameof(SelectedServer)); }), _ => IsServerSelected);

            OpenRootFolderCommand = new RelayCommand(_ => OpenFolder("Root"), _ => IsServerSelected);
            OpenIniFolderCommand = new RelayCommand(_ => OpenFolder("Ini"), _ => IsServerSelected && IsSelectedServerInstalled);
            OpenInstallFolderCommand = new RelayCommand(_ => OpenFolder("Install"), _ => IsServerSelected && IsSelectedServerInstalled);
            OpenMapSaveFolderCommand = new RelayCommand(_ => OpenFolder("MapSave"), _ => IsServerSelected && IsSelectedServerInstalled);
            VerifyServerFilesCommand = new RelayCommand(async _ => await VerifyServerFilesAsync(), _ => IsServerSelected && IsSelectedServerEditable && IsSelectedServerInstalled && !TaskSchedulerService.IsMajorOperationInProgress);

            // --- NEW COMMAND INITIALIZATION ---
            ImportServerCommand = new RelayCommand(async _ => await ImportServerAsync(), _ => IsServerSelected);
        }

        // --- NEW METHOD FOR THE COMMAND ---
        private async Task ImportServerAsync()
        {
            if (SelectedServer == null) return;

            // 1. Prompt user to select the server's root installation directory
            string? selectedFolderPath = null;
            BrowseForFolder(string.Empty, path => selectedFolderPath = path);

            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                return; // User cancelled
            }

            // 2. Locate the GameUserSettings.ini file
            string iniPath = Path.Combine(selectedFolderPath, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
            if (!File.Exists(iniPath))
            {
                MessageBox.Show($"GameUserSettings.ini not found in the selected folder.\n\nExpected path: {iniPath}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 3. Use IniFileManager to parse the file
            var iniManager = new IniFileManager(iniPath);
            await iniManager.LoadAsync();

            // 4. Extract data and populate the selected server's properties
            SelectedServer.InstallDir = selectedFolderPath;

            var sessionName = iniManager.GetValue("SessionSettings", "SessionName");
            if (!string.IsNullOrEmpty(sessionName)) SelectedServer.SessionName = sessionName;

            var portStr = iniManager.GetValue("ServerSettings", "Port");
            if (int.TryParse(portStr, out int port)) SelectedServer.Port = port;

            var queryPortStr = iniManager.GetValue("ServerSettings", "QueryPort");
            if (int.TryParse(queryPortStr, out int queryPort)) SelectedServer.QueryPort = queryPort;

            var rconPortStr = iniManager.GetValue("ServerSettings", "RCONPort");
            if (int.TryParse(rconPortStr, out int rconPort)) SelectedServer.RconPort = rconPort;

            var adminPassStr = iniManager.GetValue("ServerSettings", "ServerAdminPassword");
            if (!string.IsNullOrEmpty(adminPassStr)) SelectedServer.RconPassword = adminPassStr;

            // 5. Force the UI to refresh with the new data
            var temp = SelectedServer;
            SelectedServer = null; // This clears all bindings
            SelectedServer = temp;  // This re-applies the object, triggering all UI updates

            NotificationService.ShowInfo($"Successfully imported settings from {selectedFolderPath}");
        }

        private void OpenFolder(string folderType)
        {
            if (SelectedServer == null || string.IsNullOrWhiteSpace(SelectedServer.InstallDir)) return;

            string path = SelectedServer.InstallDir;
            bool pathExists = false;

            try
            {
                switch (folderType)
                {
                    case "Root":
                        break;
                    case "Ini":
                        path = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Saved", "Config", "WindowsServer");
                        break;
                    case "Install":
                        path = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Binaries", "Win64");
                        break;
                    case "MapSave":
                        path = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Saved", "SavedArks");
                        break;
                }

                pathExists = Directory.Exists(path);

                if (pathExists)
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show($"The directory could not be found. It may not have been created yet.\n\nPath: {path}", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while trying to open the folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseForFolder(string currentPath, System.Action<string> setPathAction)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };

            if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            {
                dialog.InitialDirectory = currentPath;
            }

            if (dialog.ShowDialog() == true)
            {
                string? folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    setPathAction(folderPath);
                }
            }
        }

        private void UpdateServersInSelectedCluster()
        {
            ServersInSelectedCluster.Clear();
            if (SelectedCluster != null)
            {
                foreach (var server in SelectedCluster.Servers)
                {
                    ServersInSelectedCluster.Add(server);
                }
            }
        }

        private void MoveServer(int direction)
        {
            if (SelectedServer == null) return;

            int oldIndex = ServersInSelectedCluster.IndexOf(SelectedServer);
            int newIndex = oldIndex + direction;

            if (newIndex < 0 || newIndex >= ServersInSelectedCluster.Count)
                return;

            var serverToMove = SelectedServer;
            ServersInSelectedCluster.Move(oldIndex, newIndex);
            SelectedServer = serverToMove;
        }


        private bool AreSelectedServerPortsValid()
        {
            if (SelectedServer == null) return false;
            return SelectedServer.Port > 0 &&
                   SelectedServer.QueryPort > 0 &&
                   SelectedServer.RconPort > 0 &&
                   !string.IsNullOrWhiteSpace(SelectedServer.RconPassword);
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

            var sessionName = iniManager.GetValue("SessionSettings", "SessionName");
            if (!string.IsNullOrEmpty(sessionName)) SelectedServer.SessionName = sessionName;

            var portStr = iniManager.GetValue("ServerSettings", "Port");
            if (int.TryParse(portStr, out int port)) SelectedServer.Port = port;

            var queryPortStr = iniManager.GetValue("ServerSettings", "QueryPort");
            if (int.TryParse(queryPortStr, out int queryPort)) SelectedServer.QueryPort = queryPort;

            var rconPortStr = iniManager.GetValue("ServerSettings", "RCONPort");
            if (int.TryParse(rconPortStr, out int rconPort)) SelectedServer.RconPort = rconPort;

            var adminPassStr = iniManager.GetValue("ServerSettings", "ServerAdminPassword");
            if (!string.IsNullOrEmpty(adminPassStr)) SelectedServer.RconPassword = adminPassStr;

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

            iniManager.SetValue("SessionSettings", "SessionName", SelectedServer.SessionName);
            iniManager.SetValue("ServerSettings", "Port", SelectedServer.Port.ToString());
            iniManager.SetValue("ServerSettings", "QueryPort", SelectedServer.QueryPort.ToString());
            iniManager.SetValue("ServerSettings", "RCONPort", SelectedServer.RconPort.ToString());
            iniManager.SetValue("ServerSettings", "ServerAdminPassword", SelectedServer.RconPassword);

            await iniManager.SaveAsync();
            NotificationService.ShowInfo("Settings saved to GameUserSettings.ini.");
        }

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
                    string destIniPath = Path.Combine(SelectedServer.InstallDir, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
                    string? destDir = Path.GetDirectoryName(destIniPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);

                    File.Copy(_config.GameUserSettingsTemplatePath, destIniPath, true);

                    var iniManager = new IniFileManager(destIniPath);
                    await iniManager.LoadAsync();
                    iniManager.SetValue("SessionSettings", "SessionName", SelectedServer.SessionName);
                    iniManager.SetValue("ServerSettings", "Port", SelectedServer.Port.ToString());
                    iniManager.SetValue("ServerSettings", "QueryPort", SelectedServer.QueryPort.ToString());
                    iniManager.SetValue("ServerSettings", "RCONPort", SelectedServer.RconPort.ToString());
                    iniManager.SetValue("ServerSettings", "ServerAdminPassword", SelectedServer.RconPassword);
                    await iniManager.SaveAsync();

                    finalMessage += " Configuration template was applied successfully.";
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
            if (SelectedCluster == null) return;
            var newServer = new ServerConfig
            {
                Id = Guid.NewGuid(),
                Name = "New Server",
                Active = true,
                MapFolder = "TheIsland_WP",
                UseApiLoader = false,
                MemoryThresholdGB = 35
            };
            SelectedCluster.Servers.Add(newServer);
            UpdateServersInSelectedCluster();
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
                UpdateServersInSelectedCluster();
                _structuralChangesMade = true;
            }
        }

        private void SaveSettings()
        {
            if (SelectedCluster != null)
            {
                SelectedCluster.Servers = ServersInSelectedCluster.ToList();
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
                MessageBox.Show("Settings saved to config.json! You have made structural changes (added/removed a server or cluster). Please restart the application for these changes to be fully reflected on the dashboard.", "Restart Recommended", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                NotificationService.ShowInfo("Settings saved to config.json successfully.");
            }

            _structuralChangesMade = false;
        }

        private async Task VerifyServerFilesAsync()
        {
            if (SelectedServer == null) return;

            var liveServer = _appViewModel.Clusters
                                          .SelectMany(c => c.Servers)
                                          .FirstOrDefault(s => s.ServerId == SelectedServer.Id);

            if (liveServer == null)
            {
                MessageBox.Show("Could not find the live server instance to verify.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show($"This will verify and repair the game files for '{liveServer.ServerName}' via SteamCMD. This can take a long time.\n\nAre you sure you want to continue?", "Confirm File Verification", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No) return;

            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                MessageBox.Show("Another major operation (like a backup or update) is already in progress. Please wait.", "Operation Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TaskSchedulerService.SetOperationLock();
            try
            {
                await UpdateManager.RunSteamCmdUpdateForServerAsync(liveServer, _config);
                await liveServer.CheckForUpdate();
                liveServer.Status = "Stopped";
                OnPropertyChanged(nameof(IsSelectedServerInstalled));
                NotificationService.ShowInfo($"File verification completed for {liveServer.ServerName}.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"An error occurred during file verification: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }
    }
}