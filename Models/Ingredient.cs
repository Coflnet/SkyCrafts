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
        /// How many units of this ingredient can be sourced from npc stock (the cheapest, instantly
        /// available channel). Quantity independent. 0 when the item is not sold by any npc.
        /// Serialized as "npcCapacity".
        /// </summary>
        public long NpcCapacity { get; set; }
        /// <summary>
        /// Coins per unit for the <see cref="NpcCapacity"/> (npc stock) portion, the capacity-weighted
        /// average npc unit price; 0 when <see cref="NpcCapacity"/> is 0. Serialized as "npcUnitPrice".
        /// </summary>
        public double NpcUnitPrice { get; set; }
        /// <summary>
        /// How many units can realistically be sourced through a competitive bazaar buy order (beyond npc
        /// stock) before the rest has to be insta-bought. Quantity independent, so a consumer can compute
        /// the npc/buy-order/insta split at any needed amount. Serialized as "buyOrderCapacity".
        /// </summary>
        public long BuyOrderCapacity { get; set; }
        /// <summary>
        /// Representative coins per unit for the <see cref="BuyOrderCapacity"/> (competitive buy order)
        /// portion, the capacity-weighted average unit price across those tranches; 0 when
        /// <see cref="BuyOrderCapacity"/> is 0. Serialized as "buyOrderUnitPrice".
        /// </summary>
        public double BuyOrderUnitPrice { get; set; }
        /// <summary>
        /// Coins per unit to insta-buy units beyond npc stock + buy orders (the marginal sell-offer
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
