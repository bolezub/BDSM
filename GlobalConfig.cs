using Newtonsoft.Json;
using System.Collections.Generic;

namespace BDSM
{
    public class GlobalConfig
    {
        [JsonProperty("servers")]
        public List<ServerConfig> Servers { get; set; } = new List<ServerConfig>();

        [JsonProperty("schedules")] // <-- New property for scheduled tasks
        public List<ScheduledTask> Schedules { get; set; } = new List<ScheduledTask>();

        [JsonProperty("mcrconPath")]
        public string McRconPath { get; set; } = string.Empty;

        [JsonProperty("steamCMDPath")]
        public string SteamCMDPath { get; set; } = string.Empty;

        [JsonProperty("backupPath")]
        public string BackupPath { get; set; } = string.Empty;

        [JsonProperty("serverIP")]
        public string ServerIP { get; set; } = string.Empty;

        [JsonProperty("rconPassword")]
        public string RconPassword { get; set; } = string.Empty;

        [JsonProperty("modArguments")]
        public string ModArguments { get; set; } = string.Empty;

        [JsonProperty("startArgumentsTemplate")]
        public string StartArgumentsTemplate { get; set; } = string.Empty;

        [JsonProperty("appId")]
        public string AppId { get; set; } = string.Empty;

        [JsonProperty("steamApiUrl")]
        public string SteamApiUrl { get; set; } = string.Empty;

        [JsonProperty("discordWebhookUrl")]
        public string discordWebhookUrl { get; set; } = string.Empty;
    }
}