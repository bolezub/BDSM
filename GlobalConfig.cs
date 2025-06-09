using Newtonsoft.Json;
using System.Collections.Generic;

namespace BDSM
{
    public class GlobalConfig
    {
        [JsonProperty("servers")]
        public List<ServerConfig> Servers { get; set; } = new List<ServerConfig>();

        [JsonProperty("mcrconPath")]
        public string McRconPath { get; set; } = string.Empty;

        [JsonProperty("steamCMDPath")]
        public string SteamCMDPath { get; set; } = string.Empty;

        [JsonProperty("backupPath")]
        public string BackupPath { get; set; } = string.Empty;

        [JsonProperty("serverIP")]
        public string ServerIP { get; set; } = string.Empty;

        // You can continue to add the rest of the properties from your config.json here
    }
}