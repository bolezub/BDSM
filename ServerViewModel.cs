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
        public ICommand ShowGraphDetailCommand { get; }

        public string ServerName => _serverConfig.Name.Replace("ASA ", "");
        public int RconPort => _serverConfig.RconPort;
        public string InstallDir => _serverConfig.InstallDir;
        public string MapFolder => _serverConfig.MapFolder; // <-- THIS IS THE NEW LINE THAT FIXES THE ERROR
        public bool DiscordNotificationsEnabled => _serverConfig.DiscordNotificationsEnabled;
        public bool IsActive => _serverConfig.Active;

        public string Status
        {
            get => _status;
            set
            {
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
                    default: return Brushes.Gray;
                }
            }
        }

        public ServerViewModel(ServerConfig serverConfig, GlobalConfig globalConfig)
        {
            _serverConfig = serverConfig;
            _globalConfig = globalConfig;
            MaxRam = serverConfig.MemoryThresholdGB;
            StartServerCommand = new RelayCommand(_ => StartServer(), _ => Status == "Stopped");
            StopServerCommand = new RelayCommand(async _ => await StopServer(), _ => Status == "Running");
            ShowGraphDetailCommand = new RelayCommand(_ => ShowGraphDetail());

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

            _ = LoadInitialDataAsync();
            Task.Run(() => MonitorServerAsync());
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
            var result = await UpdateManager.CheckForUpdateAsync(_serverConfig.InstallDir, _globalConfig.AppId, _globalConfig.SteamApiUrl);
            ServerVersion = $"Build: {result.InstalledBuild}";
            IsUpdateAvailable = result.IsUpdateAvailable;
        }

        public void StartServer()
        {
            Status = "Starting";
            string arguments = _globalConfig.StartArgumentsTemplate
                .Replace("{mapFolder}", _serverConfig.MapFolder)
                .Replace("{serverIP}", _globalConfig.ServerIP)
                .Replace("{port}", _serverConfig.Port.ToString())
                .Replace("{queryPort}", _serverConfig.QueryPort.ToString());
            arguments += " " + _globalConfig.ModArguments;
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _serverConfig.StartExecutable,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(_serverConfig.StartExecutable),
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

        private async Task StopServer()
        {
            if (Status != "Running") return;
            Status = "Stopping";
            RCON? rconClient = null;
            try
            {
                var serverEndpoint = new IPEndPoint(IPAddress.Parse(_globalConfig.ServerIP), _serverConfig.RconPort);
                rconClient = new RCON(serverEndpoint, _globalConfig.RconPassword);
                await rconClient.ConnectAsync();
                await rconClient.SendCommandAsync("DoExit");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON command to stop server {_serverConfig.Name} failed: {ex.Message}. Forcing shutdown.");
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                }
            }
            finally
            {
                rconClient?.Dispose();
            }
        }

        private async Task UpdateServerStatus()
        {
            if (Status == "Shutting Down" || Status == "Updating") return;

            var processName = Path.GetFileNameWithoutExtension(_serverConfig.ServerExecutablePath);
            var allProcesses = Process.GetProcessesByName(processName);
            Process? serverProcess = allProcesses.FirstOrDefault(p => IsCorrectServerProcess(p));

            if (serverProcess == null)
            {
                if (Status != "Stopping" && Status != "Stopped")
                {
                    Status = "Stopped";
                }
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

            if (Status == "Stopping") return;

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

                if (Status != "Update Pending")
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
                if (Status != "Update Pending")
                {
                    Status = "Starting";
                }
                CurrentPlayers = 0;
                OnlinePlayers.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON check for {_serverConfig.Name} FAILED with generic Exception: {ex.Message}");
                if (Status != "Update Pending")
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