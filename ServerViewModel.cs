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
            StartServerCommand = new RelayCommand(_ => StartServer(), _ => (Status == "Stopped" || Status == "Not Installed") && !TaskSchedulerService.IsMajorOperationInProgress && IsInstalled);
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
                Debug.WriteLine($"Password for {ServerName} loaded from .ini file.");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to kill process for {ServerName}: {ex.Message}");
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

        public async Task CheckForUpdate()
        {
            if (!IsInstalled) return;
            var result = await UpdateManager.CheckForUpdateAsync(_serverConfig.InstallDir, _globalConfig.AppId, _globalConfig.SteamApiUrl);
            ServerVersion = $"Build: {result.InstalledBuild}";
            IsUpdateAvailable = result.IsUpdateAvailable;
        }

        public void StartServer()
        {
            if (Status != "Stopped" && Status != "Not Installed") return;
            Status = "Starting";
            var mods = _serverConfig.IsClubArk ? _serverConfig.MapSpecificMods : _clusterConfig.MainModList.Union(_serverConfig.MapSpecificMods).ToList();
            string modArgument = mods.Any() ? $"-mods={string.Join(",", mods)}" : "";
            string arguments = $"{_globalConfig.StartArgumentsTemplate.Replace("{mapFolder}", _serverConfig.MapFolder).Replace("{serverIP}", _globalConfig.ServerIP).Replace("{port}", _serverConfig.Port.ToString()).Replace("{queryPort}", _serverConfig.QueryPort.ToString()).Replace("{clusterId}", _clusterConfig.ClusterId)} {modArgument}";
            string executableName = _serverConfig.UseApiLoader ? "AsaApiLoader.exe" : "ArkAscendedServer.exe";
            string executablePath = Path.Combine(_serverConfig.InstallDir, "ShooterGame", "Binaries", "Win64", executableName);
            if (!File.Exists(executablePath))
            {
                MessageBox.Show($"Error: The required server executable was not found:\n\n{executablePath}\n\nPlease ensure the server and/or API is installed correctly.", "Executable Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                Status = "Stopped";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(executablePath, arguments) { WorkingDirectory = Path.GetDirectoryName(executablePath), UseShellExecute = true, CreateNoWindow = false });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start server {_serverConfig.Name}: {ex.Message}");
                Status = "Stopped";
            }
        }

        private async Task MonitorServerAsync()
        {
            while (true)
            {
                try { await UpdateServerStatus(); }
                catch (Exception ex) { Debug.WriteLine($"!!! UNEXPECTED ERROR in monitoring loop for {FullServerName}: {ex.Message}"); }
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
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON command to stop server {FullServerName} failed: {ex.Message}. Forcing shutdown.");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON command '{command}' to server {ServerName} failed: {ex.Message}.");
                throw;
            }
            finally
            {
                rconClient?.Dispose();
            }
        }

        private bool IsValidPlayerListResponse(string rconResponse)
        {
            if (string.IsNullOrWhiteSpace(rconResponse)) return false;
            // A server that is starting but not fully ready might refuse the connection or return an error.
            // We consider any response that isn't an explicit player list (or "No Players Connected") as invalid.
            if (rconResponse.Contains("No Players Connected")) return true;
            // A valid player list contains at least one comma (e.g., "1. PlayerName, PlayerID")
            return rconResponse.Trim().Split('\n').Any(line => line.Contains(","));
        }

        public async Task UpdateServerStatus()
        {
            if (Status == "Shutting Down" || Status == "Stopping" || Status == "Update Pending" || Status == "Updating" || !IsInstalled) return;
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

                if (IsValidPlayerListResponse(response))
                {
                    bool wasJustStarted = (Status == "Starting");
                    Status = "Running";
                    if (wasJustStarted && this.DiscordNotificationsEnabled)
                    {
                        await DiscordNotifier.SendMessageAsync(_globalConfig.discordWebhookUrl, this.ServerName, "Server is online and ready.");
                    }
                    ParsePlayerInfo(response);
                    await DataLogger.LogDataPoint(this.ServerId, CpuUsage, RamUsage);
                }
                else
                {
                    Status = "Starting";
                    CurrentPlayers = 0;
                    OnlinePlayers.Clear();
                }
            }
            catch (Exception)
            {
                Status = "Starting";
                CurrentPlayers = 0;
                OnlinePlayers.Clear();
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not update performance metrics for {_serverProcess.ProcessName}. {ex.Message}");
                CpuUsage = 0; RamUsage = 0;
                _cpuCounter?.Dispose(); _cpuCounter = null;
            }
        }

        private void ParsePlayerInfo(string rconResponse)
        {
            OnlinePlayers.Clear();
            if (string.IsNullOrWhiteSpace(rconResponse) || rconResponse.Contains("No Players Connected"))
            {
                CurrentPlayers = 0; return;
            }
            var lines = rconResponse.Trim().Split('\n');
            foreach (var line in lines)
            {
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
                string processPath = process.MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(processPath)) return false;
                return Path.GetFullPath(processPath).StartsWith(Path.GetFullPath(_serverConfig.InstallDir), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}