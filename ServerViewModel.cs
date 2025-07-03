using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoreRCON;

namespace BDSM
{
    public class ServerViewModel : BaseViewModel
    {
        private readonly ServerConfig _serverConfig;
        private readonly ClusterConfig _clusterConfig;
        private readonly GlobalConfig _globalConfig;
        private PerformanceCounter? _cpuCounter;
        private string _cpuCounterInstanceName = string.Empty;
        private DateTime _lastCpuSampleTime;
        private string _status = "Unknown";
        private string _pid = string.Empty;
        private int _currentPlayers;
        private float _cpuUsage;
        private int _ramUsage;
        private string _serverVersion = "checking...";
        private bool _isUpdateAvailable = false;
        private Process? _serverProcess;
        public bool IsHidden => _serverConfig.IsHidden;

        public List<string> OnlinePlayers { get; private set; } = new List<string>();

        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand RestartServerCommand { get; }
        public ICommand EmergencyStopCommand { get; }
        public ICommand KillProcessCommand { get; }
        public ICommand SaveWorldCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand SendGenericRconCommand { get; }
        public ICommand ShowGraphDetailCommand { get; }

        public string ServerName => _serverConfig.Name.Replace("ASA ", "");
        public string FullServerName => _serverConfig.Name;
        public int RconPort => _serverConfig.RconPort;
        public string RconPassword => _serverConfig.RconPassword;
        public string InstallDir => _serverConfig.InstallDir;
        public string MapFolder => _serverConfig.MapFolder;
        public bool DiscordNotificationsEnabled => _serverConfig.DiscordNotificationsEnabled;
        public bool IsActive => _serverConfig.Active;
        public bool IsInstalled { get; private set; }

        public bool UseApiLoader => _serverConfig.UseApiLoader;

        public List<string> Aliases => _serverConfig.Aliases;

        public Guid ServerId => _serverConfig.Id;
        public Process? ServerProcess => _serverProcess;

        public string Status
        {
            get => _status;
            set
            {
                if (Application.Current == null)
                {
                    _status = value;
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusColor));
                    CommandManager.InvalidateRequerySuggested();
                });
            }
        }

        public string Pid
        {
            get => _pid;
            set { _pid = value; OnPropertyChanged(); }
        }

        public int CurrentPlayers
        {
            get => _currentPlayers;
            set { _currentPlayers = value; OnPropertyChanged(); }
        }

        public float CpuUsage
        {
            get => _cpuUsage;
            set { _cpuUsage = value; OnPropertyChanged(); }
        }

        public int RamUsage
        {
            get => _ramUsage;
            set { _ramUsage = value; OnPropertyChanged(); }
        }

        public string ServerVersion
        {
            get => _serverVersion;
            set { _serverVersion = value; OnPropertyChanged(); }
        }

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set { _isUpdateAvailable = value; OnPropertyChanged(); }
        }

        public int MaxPlayers { get; set; } = 60;
        public int MaxRam { get; set; }

        public Brush StatusColor
        {
            get
            {
                switch (Status)
                {
                    case "Running": return Brushes.LawnGreen;
                    case "Starting": return Brushes.Yellow;
                    case "Stopped": return Brushes.Red;
                    case "Stopping": return Brushes.OrangeRed;
                    case "Update Pending": return Brushes.Orange;
                    case "Shutting Down": return Brushes.DarkOrange;
                    case "Updating": return Brushes.DodgerBlue;
                    case "Not Installed": return Brushes.SlateGray;
                    case "Error": return Brushes.HotPink;
                    default: return Brushes.Gray;
                }
            }
        }

        private ServerViewModel(ServerConfig serverConfig, ClusterConfig clusterConfig, GlobalConfig globalConfig)
        {
            _serverConfig = serverConfig;
            _clusterConfig = clusterConfig;
            _globalConfig = globalConfig;
            MaxRam = serverConfig.MemoryThresholdGB > 0 ? serverConfig.MemoryThresholdGB : 35;

            string keyFilePath = Path.Combine(_serverConfig.InstallDir, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
            IsInstalled = File.Exists(keyFilePath);

            ShowGraphDetailCommand = new RelayCommand(_ => ShowGraphDetail());
            StartServerCommand = new RelayCommand(_ => StartServer(), _ => (Status == "Stopped" || Status == "Not Installed" || Status == "Error") && !TaskSchedulerService.IsMajorOperationInProgress && IsInstalled);
            StopServerCommand = new RelayCommand(async _ => await UpdateManager.PerformMaintenanceShutdownAsync(new List<ServerViewModel> { this }, _globalConfig), _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);
            RestartServerCommand = new RelayCommand(async _ => await UpdateManager.PerformScheduledRebootAsync(new List<ServerViewModel> { this }, _globalConfig), _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);
            EmergencyStopCommand = new RelayCommand(async _ => await EmergencyStopServer(), _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);
            KillProcessCommand = new RelayCommand(async _ => await KillProcessAsync(), _ => Status != "Stopped" && IsInstalled);
            SaveWorldCommand = new RelayCommand(async _ => await SendRconCommandAsync("SaveWorld"), _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);
            SendMessageCommand = new RelayCommand(async _ => await ShowMessageDialog(isGeneric: false), _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);
            SendGenericRconCommand = new RelayCommand(async _ => await ShowMessageDialog(isGeneric: true), _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);
        }

        public static async Task<ServerViewModel> CreateAsync(ServerConfig serverConfig, ClusterConfig clusterConfig, GlobalConfig globalConfig)
        {
            var viewModel = new ServerViewModel(serverConfig, clusterConfig, globalConfig);
            if (viewModel.IsInstalled)
            {
                await viewModel.LoadInitialPasswordFromIni();
            }
            else
            {
                viewModel.Status = "Not Installed";
            }
            _ = viewModel.MonitorServerAsync();
            return viewModel;
        }

        public async Task LoadInitialPasswordFromIni()
        {
            if (!IsInstalled) return;
            string iniPath = Path.Combine(_serverConfig.InstallDir, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
            if (!File.Exists(iniPath)) return;
            var iniManager = new IniFileManager(iniPath);
            await iniManager.LoadAsync();
            var adminPass = iniManager.GetValue("ServerSettings", "ServerAdminPassword");
            if (!string.IsNullOrWhiteSpace(adminPass))
            {
                _serverConfig.RconPassword = adminPass;
            }
        }

        public async Task KillProcessAsync(bool force = false)
        {
            if (!force)
            {
                var result = MessageBox.Show($"Are you sure you want to forcefully kill the process for server '{ServerName}'?\n\nThis is a last resort and can cause data loss. Use this only if the server is stuck and unresponsive.", "Confirm Kill Process", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) return;
            }

            try
            {
                _serverProcess?.Kill(true);
                NotificationService.ShowInfo($"Killed process for server {ServerName}.");
            }
            catch (Exception)
            {
            }
            Status = "Stopped";
            await Task.CompletedTask;
        }

        private async Task ShowMessageDialog(bool isGeneric)
        {
            var messageWindow = new MessageWindow(isGeneric ? "Send RCON Command" : "Send In-Game Message", isGeneric ? "Enter RCON command (e.g., ListPlayers)" : "Enter message to send to the server");
            if (messageWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(messageWindow.MessageText))
            {
                await SendRconCommandAsync(isGeneric ? messageWindow.MessageText : $"ServerChat {messageWindow.MessageText}");
            }
        }

        private async void ShowGraphDetail()
        {
            string? imagePath = await GraphGenerator.CreateGraphImageAsync(this);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                MessageBox.Show("Could not generate or find the graph image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var detailViewModel = new GraphDetailViewModel(this.ServerName, bitmap);
            var detailWindow = new GraphDetailWindow { DataContext = detailViewModel };
            detailWindow.Show();
        }

        // --- THIS IS THE NEW OVERLOADED METHOD ---
        public async Task CheckForUpdate(string? latestBuildId)
        {
            if (!IsInstalled) return;

            string installedBuild = await UpdateManager.GetInstalledBuildIdAsync(_serverConfig.InstallDir, _globalConfig.AppId);
            ServerVersion = $"Build: {installedBuild}";

            // If we couldn't get the latest build ID, we can't determine if an update is available.
            if (string.IsNullOrWhiteSpace(latestBuildId))
            {
                IsUpdateAvailable = false;
                return;
            }

            bool canTrustInstalledVersion = long.TryParse(installedBuild, out _);
            if (canTrustInstalledVersion)
            {
                IsUpdateAvailable = installedBuild != latestBuildId;
            }
            else
            {
                IsUpdateAvailable = false;
            }
        }

        // The original method is kept for the initial startup check.
        public async Task CheckForUpdate()
        {
            // On initial check, we don't have the latest build ID yet.
            string? latestBuild = await UpdateManager.GetLatestBuildIdAsync(_globalConfig.SteamApiUrl, _globalConfig.AppId);
            await CheckForUpdate(latestBuild);
        }

        public void StartServer()
        {
            if (Status != "Stopped" && Status != "Not Installed" && Status != "Error") return;
            Status = "Starting";
            var mods = _serverConfig.IsClubArk ? _serverConfig.MapSpecificMods : _clusterConfig.MainModList.Union(_serverConfig.MapSpecificMods).ToList();
            string modArgument = mods.Any() ? $"-mods={string.Join(",", mods)}" : "";
            string arguments = $"{_globalConfig.StartArgumentsTemplate.Replace("{mapFolder}", _serverConfig.MapFolder).Replace("{serverIP}", _globalConfig.ServerIP).Replace("{port}", _serverConfig.Port.ToString()).Replace("{queryPort}", _serverConfig.QueryPort.ToString()).Replace("{clusterId}", _clusterConfig.ClusterId)} {modArgument}";
            string executableName = _serverConfig.UseApiLoader ? "AsaApiLoader.exe" : "ArkAscendedServer.exe";
            string executablePath = Path.Combine(_serverConfig.InstallDir, "ShooterGame", "Binaries", "Win64", executableName);
            if (!File.Exists(executablePath))
            {
                string errorMsg = $"Error starting {ServerName}: The required server executable was not found at {executablePath}";
                LoggingService.Log(errorMsg, LogLevel.Error);
                NotificationService.ShowInfo($"Executable not found for {ServerName}. See Event Log.");
                Status = "Error";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(executablePath, arguments) { WorkingDirectory = Path.GetDirectoryName(executablePath), UseShellExecute = true, CreateNoWindow = false });
            }
            catch (Exception)
            {
                Status = "Stopped";
            }
        }

        private async Task MonitorServerAsync()
        {
            while (true)
            {
                try { await UpdateServerStatus(); }
                catch (Exception) { }
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        public async Task EmergencyStopServer()
        {
            if (Status != "Running") return;
            Status = "Stopping";
            try
            {
                await SendRconCommandAsync("DoExit");
            }
            catch (Exception)
            {
                _serverProcess?.Kill();
            }
        }

        public async Task<string> SendRconCommandAsync(string command)
        {
            if (Status == "Stopped" || Status == "Not Installed" || string.IsNullOrWhiteSpace(_serverConfig.RconPassword)) return string.Empty;
            RCON? rconClient = null;
            try
            {
                var serverEndpoint = new IPEndPoint(IPAddress.Parse(_globalConfig.ServerIP), _serverConfig.RconPort);
                rconClient = new RCON(serverEndpoint, _serverConfig.RconPassword);
                await rconClient.ConnectAsync();
                return await rconClient.SendCommandAsync(command);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                rconClient?.Dispose();
            }
        }

        public async Task UpdateServerStatus()
        {
            if (Status == "Shutting Down" || Status == "Updating" || !IsInstalled) return;

            var processName = "ArkAscendedServer";
            var allProcesses = Process.GetProcessesByName(processName);
            Process? serverProcess = allProcesses.FirstOrDefault(p => IsCorrectServerProcess(p));

            if (serverProcess == null)
            {
                if (Status != "Stopped") Status = "Stopped";
                Pid = string.Empty; CurrentPlayers = 0; OnlinePlayers.Clear(); CpuUsage = 0; RamUsage = 0;
                _serverProcess = null; _cpuCounter?.Dispose(); _cpuCounter = null;
                return;
            }

            _serverProcess = serverProcess;
            Pid = $"PID {serverProcess.Id}";
            UpdatePerformanceMetrics();

            try
            {
                string response = await SendRconCommandAsync("listplayers");
                response = response.Trim();

                if (!string.IsNullOrWhiteSpace(response))
                {
                    if (Status != "Running")
                    {
                        bool wasJustStarting = Status == "Starting" || Status == "Unknown";
                        Status = "Running";
                        if (wasJustStarting && this.DiscordNotificationsEnabled && Status != "Update Pending")
                        {
                            await DiscordNotifier.SendMessageAsync(_globalConfig.discordWebhookUrl, this.ServerName, "Server is online and ready.");
                        }
                    }

                    ParsePlayerList(response);
                    await DataLogger.LogDataPoint(this.ServerId, CpuUsage, RamUsage);
                }
                else
                {
                    if (Status != "Update Pending") Status = "Starting";
                }
            }
            catch (Exception)
            {
                if (Status != "Update Pending") Status = "Starting";
                OnlinePlayers.Clear();
                CurrentPlayers = 0;
            }
        }

        private void UpdatePerformanceMetrics()
        {
            if (_serverProcess == null || _serverProcess.HasExited) return;
            try
            {
                _serverProcess.Refresh();
                RamUsage = (int)Math.Round(_serverProcess.WorkingSet64 / (1024.0 * 1024.0 * 1024.0));

                if (_cpuCounter == null)
                {
                    var category = new PerformanceCounterCategory("Process");
                    var instanceNames = category.GetInstanceNames().Where(inst => inst.StartsWith(_serverProcess.ProcessName)).ToList();
                    foreach (var name in instanceNames)
                    {
                        using (var counter = new PerformanceCounter("Process", "ID Process", name, true))
                        {
                            if ((int)counter.RawValue == _serverProcess.Id)
                            {
                                _cpuCounterInstanceName = name;
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(_cpuCounterInstanceName))
                    {
                        _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _cpuCounterInstanceName, true);
                        _cpuCounter.NextValue();
                    }
                    else { CpuUsage = 0; return; }
                }

                if (DateTime.UtcNow - _lastCpuSampleTime > TimeSpan.FromSeconds(2))
                {
                    float rawUsage = _cpuCounter?.NextValue() ?? 0;
                    CpuUsage = rawUsage / Environment.ProcessorCount;
                    _lastCpuSampleTime = DateTime.UtcNow;
                }
            }
            catch (Exception)
            {
                CpuUsage = 0; RamUsage = 0;
                _cpuCounter?.Dispose(); _cpuCounter = null;
            }
        }

        private void ParsePlayerList(string rconResponse)
        {
            OnlinePlayers.Clear();

            if (string.IsNullOrWhiteSpace(rconResponse) || rconResponse == "No Players Connected")
            {
                CurrentPlayers = 0;
                return;
            }

            var lines = rconResponse.Trim().Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains(",")) continue;

                var parts = line.Split(',');
                if (parts.Length > 0 && parts[0].IndexOf(' ') != -1)
                {
                    OnlinePlayers.Add(parts[0].Substring(parts[0].IndexOf(' ') + 1).Trim());
                }
            }
            CurrentPlayers = OnlinePlayers.Count;
        }

        private bool IsCorrectServerProcess(Process process)
        {
            try
            {
                if (process.HasExited) return false;
                string? processPath = process.MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(processPath)) return false;
                return Path.GetFullPath(processPath).StartsWith(Path.GetFullPath(_serverConfig.InstallDir), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}