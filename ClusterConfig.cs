using Newtonsoft.Json;
using System.Collections.Generic;

namespace BDSM
{
    public class ClusterConfig
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "New Cluster";

        [JsonProperty("clusterId")]
        public string ClusterId { get; set; } = "arkcluster";

        [JsonProperty("mainModList")]
        public List<int> MainModList { get; set; } = new List<int>();

        [JsonProperty("servers")]
        public List<ServerConfig> Servers { get; set; } = new List<ServerConfig>();
    }
}