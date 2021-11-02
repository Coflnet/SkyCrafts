using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Coflnet.Sky.Crafts.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
    public class Tier
    {
        [JsonPropertyName("tier")]
        public int TierId { get; set; }

        [JsonPropertyName("amountRequired")]
        public int AmountRequired { get; set; }

        [JsonPropertyName("unlocks")]
        public List<string> Unlocks { get; set; }
    }

    public class ItemCollection
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("maxTiers")]
        public int MaxTiers { get; set; }

        [JsonPropertyName("tiers")]
        public List<Tier> Tiers { get; set; }
    }

    public class Collection
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("items")]
        public Dictionary<string,ItemCollection> Items { get; set; }
    }

    public class CollectionsRoot
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("lastUpdated")]
        public long LastUpdated { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("collections")]
        public Dictionary<string,Collection> Collections { get; set; }
    }
}
