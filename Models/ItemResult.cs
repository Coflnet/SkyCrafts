using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models
{
    public class ProfitableCraft
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public double SellPrice { get; set; }
        public double CraftCost { get; set; }
        public IEnumerable<Ingredient> Ingredients { get; set; }
        public RequiredCollection ReqCollection { get; set; }
        public RequiredCollection ReqSlayer { get; set; }
        public RequiredSkill ReqSkill { get; set; }
        public string Type { get; set; }
        public double Volume { get; set; }
        public float Median { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public class RequiredSkill
    {
        public string Name { get; set; }
        public int Level { get; set; }
    }
}
