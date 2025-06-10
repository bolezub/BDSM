using Newtonsoft.Json;

public class ServerConfig
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("installDir")]
    public string InstallDir { get; set; } = string.Empty;

    [JsonProperty("serverExecutablePath")]
    public string ServerExecutablePath { get; set; } = string.Empty;

    [JsonProperty("startExecutable")]
    public string StartExecutable { get; set; } = string.Empty;

    [JsonProperty("mapFolder")] // <-- Added this property
    public string MapFolder { get; set; } = string.Empty;

    [JsonProperty("port")]
    public int Port { get; set; }

    [JsonProperty("queryPort")]
    public int QueryPort { get; set; }

    [JsonProperty("rconPort")]
    public int RconPort { get; set; }

    [JsonProperty("active")]
    public bool IsActive { get; set; }

    [JsonProperty("hidden")]
    public bool IsHidden { get; set; }

    [JsonProperty("memoryThresholdGB")]
    public int MemoryThresholdGB { get; set; }
}