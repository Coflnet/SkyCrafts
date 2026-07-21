namespace Coflnet.Sky.Crafts.Models
{
    public class Ingredient
    {
        public string ItemId { get; set; }
        public long Count { get; set; }
        public double Cost { get; set; }
        public double BuyOrderCost { get; set; }
        public double CraftCost { get; set; }
        public string Type { get; set; }
        /// <summary>
        /// How many units of this ingredient can realistically be sourced through the cheap channels
        /// (npc stock + a competitive bazaar buy order) before the rest has to be insta-bought. Quantity
        /// independent, so a consumer can compute the buy-order/insta split at any needed amount.
        /// Serialized as "buyOrderCapacity".
        /// </summary>
        public long BuyOrderCapacity { get; set; }
        /// <summary>
        /// Representative coins per unit for the <see cref="BuyOrderCapacity"/> (npc + buy-order) portion,
        /// the capacity-weighted average unit price across those tranches; 0 when
        /// <see cref="BuyOrderCapacity"/> is 0. Serialized as "buyOrderUnitPrice".
        /// </summary>
        public double BuyOrderUnitPrice { get; set; }
        /// <summary>
        /// Coins per unit to insta-buy units beyond <see cref="BuyOrderCapacity"/> (the marginal sell-offer
        /// price), i.e. the cheapest "insta" tranche's unit price; 0 when there is no insta tranche.
        /// Serialized as "instaBuyUnitPrice".
        /// </summary>
        public double InstaBuyUnitPrice { get; set; }


        public static implicit operator Ingredient(string tag)
        {
            return new Ingredient()
            {
                ItemId = tag,
                Count = 1
            };
        }
    }
}
