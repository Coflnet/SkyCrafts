using System.Text.Json.Serialization;

namespace Coflnet.Sky.Crafts.Models
{
    public class PriceResponse
    {
        [JsonPropertyName("sell")]
        public double SellPrice { get; set; }
        [JsonPropertyName("buy")]
        public double BuyPrice { get; set; }
    }
}
