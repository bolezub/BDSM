using Newtonsoft.Json;

namespace BDSM
{
    public class WatchdogConfig
    {
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = false;

        [JsonProperty("scanIntervalSeconds")]
        public int ScanIntervalSeconds { get; set; } = 60;

        [JsonProperty("graphPostIntervalMinutes")]
        public int GraphPostIntervalMinutes { get; set; } = 15;

        [JsonProperty("textMessageId")]
        public string TextMessageId { get; set; } = string.Empty;

        [JsonProperty("graphMessageId")]
        public string GraphMessageId { get; set; } = string.Empty;
    }
}