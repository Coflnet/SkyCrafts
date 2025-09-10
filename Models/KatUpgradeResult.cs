namespace Coflnet.Sky.Crafts.Models
{
    /// <summary>
    /// Represents a "Kat flip"
    /// </summary>
    public class KatUpgradeResult
    {
        public KatUpgradeCost CoreData { get; set; }
        public string OriginAuction { get; set; }
        public double MaterialCost { get; set; }
        public double UpgradeCost { get; set; }
        public double Profit { get; set; }
        public Api.Client.Model.Tier TargetRarity { get; set; }
        public string ReferenceAuction { get; set; }
        public long PurchaseCost { get; set; }
        public string OriginAuctionName { get; set; }
    }
}
