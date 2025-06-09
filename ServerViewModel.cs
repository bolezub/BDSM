using System.Windows.Media;
using BDSM;

namespace BDSM
{
    public class ServerViewModel
    {
        private readonly ServerConfig _serverConfig;

        public string ServerName => _serverConfig.Name.Replace("ASA ", "");

        // FIX: Add default values to fix the warnings
        public string Status { get; set; } = "Unknown";
        public string Pid { get; set; } = string.Empty;
        public string ServerVersion { get; set; } = string.Empty;
        public int CurrentPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int CpuUsage { get; set; }
        public int RamUsage { get; set; }
        public int MaxRam { get; set; }

        public Brush StatusColor
        {
            get
            {
                switch (Status)
                {
                    case "Running":
                        return Brushes.Green;
                    case "Starting":
                        return Brushes.Yellow;
                    case "Stopped":
                        return Brushes.Red;
                    default:
                        return Brushes.Gray;
                }
            }
        }

        public ServerViewModel(ServerConfig serverConfig)
        {
            _serverConfig = serverConfig;
            MaxPlayers = 60;
            MaxRam = serverConfig.MemoryThresholdGB;
            ServerVersion = "Server version 66.05"; // Placeholder
        }
    }
}