using System;
using Coflnet.Sky.Crafts.Models;
using ApiModel = Coflnet.Sky.Api.Client.Model;

namespace Coflnet.Sky.Crafts.Models
{
    public class GeorgeFlipResult
    {
        public string ItemTag { get; set; }
        public string ItemName { get; set; }
        public ApiModel.Tier Rarity { get; set; }
        public string OriginAuction { get; set; }
        public string ReferenceAuction { get; set; }
        public double PurchaseCost { get; set; }
        public double TargetPrice { get; set; }
        public double Profit { get; set; }
        public double ProfitMargin { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
