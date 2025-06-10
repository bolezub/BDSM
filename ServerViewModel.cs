using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CoreRCON;

namespace BDSM
{
    public class ServerViewModel : BaseViewModel
    {
        private readonly ServerConfig _serverConfig;
        private readonly GlobalConfig _globalConfig;

        // --- New fields for CPU calculation ---
        private PerformanceCounter? _cpuCounter;
        private DateTime _lastCpuSampleTime;
        // ------------------------------------

        private string _status = "Unknown";
        private string _pid = string.Empty;
        private int _currentPlayers;
        private float _cpuUsage; // Changed to float for more precision
        private int _ramUsage;
        private Process? _serverProcess;

        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }

        public string ServerName => _serverConfig.Name.Replace("ASA ", "");
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); CommandManager.InvalidateRequerySuggested(); }
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

        public float CpuUsage // Changed to float
        {
            get => _cpuUsage;
            set { _cpuUsage = value; OnPropertyChanged(); }
        }

        public int RamUsage
        {
            get => _ramUsage;
            set { _ramUsage = value; OnPropertyChanged(); }
        }

        public int MaxPlayers { get; set; } = 60;
        public string ServerVersion { get; set; } = "Server version 66.05";
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
            Task.Run(() => MonitorServerAsync());
        }

        private void StartServer()
        {
            if (Status != "Stopped") return;
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
                    Debug.WriteLine($"!!! UNEXPECTED ERROR in monitoring loop for {_serverConfig.Name}: {ex.Message}");
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
            var processName = Path.GetFileNameWithoutExtension(_serverConfig.ServerExecutablePath);
            var allProcesses = Process.GetProcessesByName(processName);
            Process? serverProcess = allProcesses.FirstOrDefault(p => IsCorrectServerProcess(p));

            if (serverProcess == null)
            {
                Status = "Stopped";
                Pid = string.Empty;
                CurrentPlayers = 0;
                CpuUsage = 0;
                RamUsage = 0;
                _serverProcess = null;
                _cpuCounter?.Dispose(); // Clean up the counter when the process stops
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
                Status = "Running";
                ParsePlayerCount(response);
            }
            catch (SocketException)
            {
                Status = "Starting";
                CurrentPlayers = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RCON check for {_serverConfig.Name} FAILED with generic Exception: {ex.Message}");
                Status = "Starting";
                CurrentPlayers = 0;
            }
            finally
            {
                rconClient?.Dispose();
            }
        }

        // --- This method is now updated for CPU ---
        private void UpdatePerformanceMetrics()
        {
            if (_serverProcess == null || _serverProcess.HasExited) return;

            _serverProcess.Refresh();
            RamUsage = (int)(_serverProcess.WorkingSet64 / (1024 * 1024 * 1024));

            // Initialize the CPU counter if it's not already
            if (_cpuCounter == null)
            {
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _serverProcess.ProcessName, true);
                _cpuCounter.NextValue(); // First call is always 0, so we call it to get it started.
                _lastCpuSampleTime = DateTime.UtcNow;
                CpuUsage = 0; // Display 0 on the first run
                return;
            }

            // Only calculate CPU usage every few seconds to get a stable reading
            if (DateTime.UtcNow - _lastCpuSampleTime > TimeSpan.FromSeconds(2))
            {
                // The value is the total % usage across all cores.
                // We divide by the number of cores to get a value comparable to Task Manager.
                float rawUsage = _cpuCounter.NextValue();
                CpuUsage = rawUsage / Environment.ProcessorCount;
                _lastCpuSampleTime = DateTime.UtcNow;
            }
        }
        // --- End of change ---

        private void ParsePlayerCount(string rconResponse)
        {
            if (string.IsNullOrWhiteSpace(rconResponse) || rconResponse.Contains("No Players Connected"))
            {
                CurrentPlayers = 0;
            }
            else
            {
                CurrentPlayers = rconResponse.Trim().Split('\n').Length;
            }
        }

        private bool IsCorrectServerProcess(Process process)
        {
            try
            {
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