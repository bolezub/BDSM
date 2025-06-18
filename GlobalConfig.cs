using Newtonsoft.Json;
using System.Collections.Generic;

namespace BDSM
{
    public class GlobalConfig
    {
        [JsonProperty("clusters")]
        public List<ClusterConfig> Clusters { get; set; } = new List<ClusterConfig>();

        [JsonProperty("schedules")]
        public List<ScheduledTask> Schedules { get; set; } = new List<ScheduledTask>();

        [JsonProperty("watchdog")]
        public WatchdogConfig Watchdog { get; set; } = new WatchdogConfig();

        [JsonProperty("gameUserSettingsTemplatePath")]
        public string GameUserSettingsTemplatePath { get; set; } = string.Empty;

        [JsonProperty("availableMaps")]
        public List<string> AvailableMaps { get; set; } = new List<string>();

        [JsonProperty("botToken")]
        public string BotToken { get; set; } = string.Empty;

        [JsonProperty("backupIntervalMinutes")]
        public int BackupIntervalMinutes { get; set; } = 30;

        [JsonProperty("backupRetryIntervalMinutes")]
        public int BackupRetryIntervalMinutes { get; set; } = 5;

        [JsonProperty("updateCheckIntervalMinutes")]
        public int UpdateCheckIntervalMinutes { get; set; } = 15;

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

        [JsonProperty("watchdogDiscordWebhookUrl")]
        public string WatchdogDiscordWebhookUrl { get; set; } = string.Empty;
    }
}