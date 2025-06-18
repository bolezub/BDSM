using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CoreRCON;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace BDSM
{
    public class ServerViewModel : BaseViewModel
    {
        private readonly ServerConfig _serverConfig;
        private readonly ClusterConfig _clusterConfig;
        private readonly GlobalConfig _globalConfig;

        private PerformanceCounter? _cpuCounter;
        private DateTime _lastCpuSampleTime;

        private string _status = "Unknown";
        private string _pid = string.Empty;
        private int _currentPlayers;
        private float _cpuUsage;
        private int _ramUsage;
        private string _serverVersion = "checking...";
        private bool _isUpdateAvailable = false;
        private Process? _serverProcess;

        public List<string> OnlinePlayers { get; private set; } = new List<string>();

        public ObservableCollection<ISeries> Series { get; set; }
        public Axis[] XAxes { get; set; }
        public Axis[] YAxes { get; set; }

        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand RestartServerCommand { get; }
        public ICommand EmergencyStopCommand { get; }
        public ICommand SaveWorldCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand SendGenericRconCommand { get; }
        public ICommand ShowGraphDetailCommand { get; }
        // NEW COMMAND
        public ICommand KillProcessCommand { get; }

        public string ServerName => _serverConfig.Name.Replace("ASA ", "");
        public int RconPort => _serverConfig.RconPort;
        public string InstallDir => _serverConfig.InstallDir;
        public string MapFolder => _serverConfig.MapFolder;
        public bool DiscordNotificationsEnabled => _serverConfig.DiscordNotificationsEnabled;
        public bool IsActive => _serverConfig.Active;
        public bool IsInstalled { get; private set; }

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

        public ServerViewModel(ServerConfig serverConfig, ClusterConfig clusterConfig, GlobalConfig globalConfig)
        {
            _serverConfig = serverConfig;
            _clusterConfig = clusterConfig;
            _globalConfig = globalConfig;
            MaxRam = serverConfig.MemoryThresholdGB;

            string keyFilePath = Path.Combine(_serverConfig.InstallDir, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
            IsInstalled = File.Exists(keyFilePath);

            ShowGraphDetailCommand = new RelayCommand(_ => ShowGraphDetail());

            StartServerCommand = new RelayCommand(
                _ => StartServer(),
                _ => Status == "Stopped" && !TaskSchedulerService.IsMajorOperationInProgress && IsInstalled);

            StopServerCommand = new RelayCommand(
                async _ => await UpdateManager.PerformMaintenanceShutdownAsync(new List<ServerViewModel> { this }, _globalConfig),
                _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);

            RestartServerCommand = new RelayCommand(
                async _ => await UpdateManager.PerformScheduledRebootAsync(new List<ServerViewModel> { this }, _globalConfig),
                _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);

            EmergencyStopCommand = new RelayCommand(
                async _ => await EmergencyStopServer(),
                _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);

            SaveWorldCommand = new RelayCommand(
                async _ => await SendRconCommandAsync("SaveWorld"),
                _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);

            SendMessageCommand = new RelayCommand(
                async _ => await ShowMessageDialog(isGeneric: false),
                _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);

            SendGenericRconCommand = new RelayCommand(
                async _ => await ShowMessageDialog(isGeneric: true),
                _ => Status == "Running" && !TaskSchedulerService.IsMajorOperationInProgress);

            // NEW: Initialize Kill command
            KillProcessCommand = new RelayCommand(
                async _ => await KillProcessAsync(),
                _ => Status != "Stopped" && IsInstalled);


            Series = new ObservableCollection<ISeries>();
            XAxes = new Axis[]
            {
                new Axis { Labeler = value => value >= 0 ? new DateTime((long)value).ToString("HH:mm") : "", UnitWidth = TimeSpan.FromHours(1).Ticks }
            };
            YAxes = new Axis[]
            {
                new Axis { Name = "CPU (%)", MinLimit = 0, MaxLimit = 100 },
                new Axis { Name = "RAM (GB)", Position = LiveChartsCore.Measure.AxisPosition.End, MinLimit = 0 }
            };

            Task.Run(async () =>
            {
                try
                {
                    if (!IsInstalled)
                    {
                        Status = "Not Installed";
                        return;
                    }
                    await LoadInitialDataAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"!!! ERROR during initial data load for {ServerName}: {ex.Message}");
                }

                await MonitorServerAsync();
            });
        }

        private async Task KillProcessAsync()
        {
            var result = MessageBox.Show($"Are you sure you want to forcefully kill the process for server '{ServerName}'?\n\nThis is a last resort and can cause data loss. Use this only if the server is stuck and unresponsive.", "Confirm Kill Process", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                return;
            }

            // First, try to kill the cached process object if we have it
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill(true); // Kill entire process tree
                    NotificationService.ShowInfo($"Killed process {_serverProcess.Id} for server {ServerName}.");
                    Status = "Stopped";
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to kill cached process: {ex.Message}");
                }
            }

            // If that fails or we don't have a cached process, find it by path and kill it
            var processName = "ArkAscendedServer";
            var allProcesses = Process.GetProcessesByName(processName);
            var processToKill = allProcesses.FirstOrDefault(p => IsCorrectServerProcess(p));

            if (processToKill != null)
            {
                try
                {
                    processToKill.Kill(true);
                    NotificationService.ShowInfo($"Killed process {processToKill.Id} for server {ServerName}.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to kill process for {ServerName}. Reason: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                NotificationService.ShowInfo($"Could not find a running process for {ServerName} to kill. Resetting status.");
            }

            // Regardless of outcome, reset the status
            Status = "Stopped";
        }


        private async Task LoadInitialDataAsync()
        {
            var data = await DataLogger.GetPerformanceDataAsync(_serverConfig.Name, 24);
            var cpuValues = data.Select(d => new DateTimePoint(d.Timestamp, d.CpuUsage)).ToList();
            var ramValues = data.Select(d => new DateTimePoint(d.Timestamp, d.RamUsage)).ToList();

            Series.Add(new LineSeries<DateTimePoint>
            {
                Name = "CPU Usage (%)",
                Values = cpuValues,
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                ScalesYAt = 0
            });

            Series.Add(new LineSeries<DateTimePoint>
            {
                Name = "Memory Usage (GB)",
                Values = ramValues,
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                ScalesYAt = 1
            });

            await CheckForUpdate();
        }

        private async Task ShowMessageDialog(bool isGeneric)
        {
            var windowTitle = isGeneric ? "Send RCON Command" : "Send In-Game Message";
            var messageHint = isGeneric ? "Enter RCON command (e.g., ListPlayers)" : "Enter message to send to the server";

            var messageWindow = new MessageWindow(windowTitle, messageHint);

            bool? result = messageWindow.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(messageWindow.MessageText))
            {
                var commandToSend = isGeneric ? messageWindow.MessageText : $"ServerChat {messageWindow.MessageText}";
                await SendRconCommandAsync(commandToSend);
            }
        }

        private void ShowGraphDetail()
        {
            var detailViewModel = new GraphDetailViewModel(this.ServerName, this.Series, this.XAxes, this.YAxes);
            var detailWindow = new GraphDetailWindow
            {
                DataContext = detailViewModel
            };
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
            Status = "Starting";

            var mods = new List<int>();
            if (_serverConfig.IsClubArk)
            {
                mods.AddRange(_serverConfig.MapSpecificMods);
            }
            else
            {
                mods.AddRange(_clusterConfig.MainModList);
                mods.AddRange(_serverConfig.MapSpecificMods);
            }

            var distinctMods = mods.Distinct();
            string modArgument = distinctMods.Any() ? $"-mods={string.Join(",", distinctMods)}" : "";

            string arguments = _globalConfig.StartArgumentsTemplate
                .Replace("{mapFolder}", _serverConfig.MapFolder)
                .Replace("{serverIP}", _globalConfig.ServerIP)
                .Replace("{port}", _serverConfig.Port.ToString())
                .Replace("{queryPort}", _serverConfig.QueryPort.ToString())
                .Replace("{clusterId}", _clusterConfig.ClusterId);

            arguments += " " + modArgument;

            string executableName = _serverConfig.UseApiLoader ? "AsaApiLoader.exe" : "ArkAscendedServer.exe";
            string executablePath = Path.Combine(_serverConfig.InstallDir, "ShooterGame", "Binaries", "Win64", executableName);

            if (!File.Exists(executablePath))
            {
                Debug.WriteLine($"Executable not found at {executablePath}. Cannot start server {_serverConfig.Name}.");
                MessageBox.Show($"Error: The required server executable was not found:\n\n{executablePath}\n\nPlease ensure the server and/or API is installed correctly.", "Executable Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                Status = "Stopped";
                return;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = true,
                CreateNoWindow = false
            };
            try
            {
                Process.Start(processStartInfo);
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
                try
                {
                    await UpdateServerStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"!!! UNEXPECTED ERROR in monitoring loop for {_serverConfig.Name}: {ex.Message}");
                }
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
                Debug.WriteLine($"RCON command to stop server {_serverConfig.Name} failed: {ex.Message}. Forcing shutdown.");
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                }
            }
        }

        public async Task SendRconCommandAsync(string command)
        {
            if (Status != "Running" && Status != "Stopping") return;

            RCON? rconClient = null;
            try
            {
                var serverEndpoint = new IPEndPoint(IPAddress.Parse(_globalConfig.ServerIP), _serverConfig.RconPort);
                rconClient = new RCON(serverEndpoint, _globalConfig.RconPassword);
                await rconClient.ConnectAsync();
                await rconClient.SendCommandAsync(command);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON command '{command}' to server {ServerName} failed: {ex.Message}.");
            }
            finally
            {
                rconClient?.Dispose();
            }
        }

        private async Task UpdateServerStatus()
        {
            if (Status == "Shutting Down" || Status == "Updating" || !IsInstalled) return;

            var processName = "ArkAscendedServer";
            var allProcesses = Process.GetProcessesByName(processName);
            Process? serverProcess = allProcesses.FirstOrDefault(p => IsCorrectServerProcess(p));

            if (serverProcess == null)
            {
                if (Status != "Stopped") Status = "Stopped";
                Pid = string.Empty;
                CurrentPlayers = 0;
                OnlinePlayers.Clear();
                CpuUsage = 0;
                RamUsage = 0;
                _serverProcess = null;
                _cpuCounter?.Dispose();
                _cpuCounter = null;
                return;
            }

            _serverProcess = serverProcess;
            Pid = $"PID {serverProcess.Id}";
            UpdatePerformanceMetrics();

            RCON? rconClient = null;
            try
            {
                var serverEndpoint = new IPEndPoint(IPAddress.Parse(_globalConfig.ServerIP), _serverConfig.RconPort);
                rconClient = new RCON(serverEndpoint, _globalConfig.RconPassword);
                await rconClient.ConnectAsync();
                string response = await rconClient.SendCommandAsync("listplayers");

                bool wasJustStarted = (Status == "Starting");

                if (Status != "Update Pending" && Status != "Stopping")
                {
                    Status = "Running";
                }

                ParsePlayerInfo(response);

                if (wasJustStarted && this.DiscordNotificationsEnabled)
                {
                    await DiscordNotifier.SendMessageAsync(_globalConfig.discordWebhookUrl, this.ServerName, "Server is online and ready.");
                }

                await DataLogger.LogDataPoint(_serverConfig.Name, CpuUsage, RamUsage);
            }
            catch (SocketException)
            {
                if (Status != "Starting" && Status != "Stopping" && Status != "Update Pending")
                {
                    Status = "Starting";
                }
                CurrentPlayers = 0;
                OnlinePlayers.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON check for {_serverConfig.Name} FAILED with generic Exception: {ex.Message}");
                if (Status != "Starting" && Status != "Stopping" && Status != "Update Pending")
                {
                    Status = "Starting";
                }
                CurrentPlayers = 0;
                OnlinePlayers.Clear();
            }
            finally
            {
                rconClient?.Dispose();
            }
        }

        private void UpdatePerformanceMetrics()
        {
            if (_serverProcess == null || _serverProcess.HasExited) return;

            try
            {
                _serverProcess.Refresh();
                RamUsage = (int)(_serverProcess.WorkingSet64 / (1024 * 1024 * 1024));

                if (_cpuCounter == null)
                {
                    _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _serverProcess.ProcessName, true);
                    _cpuCounter.NextValue();
                    _lastCpuSampleTime = DateTime.UtcNow;
                    CpuUsage = 0;
                    return;
                }

                if (DateTime.UtcNow - _lastCpuSampleTime > TimeSpan.FromSeconds(2))
                {
                    float rawUsage = _cpuCounter.NextValue();
                    CpuUsage = rawUsage / Environment.ProcessorCount;
                    _lastCpuSampleTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not update performance metrics for {_serverProcess.ProcessName}. {ex.Message}");
                CpuUsage = 0;
                RamUsage = 0;
                _cpuCounter?.Dispose();
                _cpuCounter = null;
            }
        }

        private void ParsePlayerInfo(string rconResponse)
        {
            OnlinePlayers.Clear();

            if (string.IsNullOrWhiteSpace(rconResponse) || rconResponse.Contains("No Players Connected"))
            {
                CurrentPlayers = 0;
                return;
            }

            var lines = rconResponse.Trim().Split('\n');
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length > 0)
                {
                    int spaceIndex = parts[0].IndexOf(' ');
                    if (spaceIndex != -1)
                    {
                        string name = parts[0].Substring(spaceIndex + 1).Trim();
                        OnlinePlayers.Add(name);
                    }
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

                string installDir = Path.GetFullPath(_serverConfig.InstallDir);
                return Path.GetFullPath(processPath).StartsWith(installDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}