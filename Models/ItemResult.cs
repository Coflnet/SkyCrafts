using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models
{
    public class ProfitableCraft
    {
        public string ItemId { get; set; }
        public double SellPrice { get; set; }
        public double CraftCost { get; set; }
        public IEnumerable<Ingredient> Ingredients { get; set; }
        public RequiredCollection ReqCollection { get; set; }
    }
}
