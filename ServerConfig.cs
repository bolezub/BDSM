using Newtonsoft.Json;
using System;
using System.Collections.Generic;

public class ServerConfig
{
    [JsonProperty("id")]
    public Guid Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("sessionName")]
    public string SessionName { get; set; } = string.Empty;

    [JsonIgnore]
    public string RconPassword { get; set; } = string.Empty;

    [JsonProperty("isClubArk")]
    public bool IsClubArk { get; set; } = false;

    [JsonProperty("useApiLoader")]
    public bool UseApiLoader { get; set; } = true;

    [JsonProperty("installDir")]
    public string InstallDir { get; set; } = string.Empty;

    [JsonProperty("mapFolder")]
    public string MapFolder { get; set; } = string.Empty;

    [JsonProperty("port")]
    public int Port { get; set; }

    [JsonProperty("queryPort")]
    public int QueryPort { get; set; }

    [JsonProperty("rconPort")]
    public int RconPort { get; set; }

    [JsonProperty("mapSpecificMods")]
    public List<int> MapSpecificMods { get; set; } = new List<int>();

    [JsonProperty("discordNotificationsEnabled")]
    public bool DiscordNotificationsEnabled { get; set; }

    [JsonProperty("memoryThresholdGB")]
    public int MemoryThresholdGB { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }

    [JsonProperty("hidden")]
    public bool IsHidden { get; set; }
    
    [JsonProperty("aliases")]
    public List<string> Aliases { get; set; } = new List<string>();
}