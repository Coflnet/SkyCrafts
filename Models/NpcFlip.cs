using System;

namespace Coflnet.Sky.Crafts.Models
{
    public class NpcFlip
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public double BuyPrice { get; set; }
        public double NpcSellPrice { get; set; }
        public double Profit { get; set; }
        public double ProfitMargin { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
