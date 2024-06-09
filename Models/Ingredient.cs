namespace Coflnet.Sky.Crafts.Models
{
    public class Ingredient
    {
        public string ItemId { get; set; }
        public long Count { get; set; }
        public double Cost { get; set; }
        public double CraftCost { get; set; }
        public string Type { get; set; }


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