using Newtonsoft.Json;

public class ServerConfig
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty; // Add default value

    [JsonProperty("installDir")]
    public string InstallDir { get; set; } = string.Empty; // Add default value

    [JsonProperty("active")]
    public bool IsActive { get; set; }

    [JsonProperty("hidden")]
    public bool IsHidden { get; set; }

    [JsonProperty("memoryThresholdGB")]
    public int MemoryThresholdGB { get; set; }
}