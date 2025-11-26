using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models;

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

public class ReverseNpcFlip
{
    public string ItemId { get; set; }
    public string ItemName { get; set; }
    public double NpcBuyPrice { get; set; }
    public double SellPrice { get; set; }
    public double Profit { get; set; }
    public double ProfitMargin { get; set; }
    public List<Cost> Costs { get; set; }
    public double Volume { get; set; }
    public DateTime LastUpdated { get; set; }

    public class Cost
    {
        public string ItemName { get; set; }
        public double Price { get; set; }
        public int Amount { get; set; }
        public string ItemTag { get; set; }
    }
}
