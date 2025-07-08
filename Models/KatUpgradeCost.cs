using Newtonsoft.Json;

namespace Coflnet.Sky.Crafts.Models
{
    public class KatUpgradeCost
    {
        /// <summary>
        /// The name of the pet
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Rarity in corelation with cost
        /// </summary>
        [JsonProperty("baseRarity")]
        public Api.Client.Model.Tier BaseRarity { get; set; }

        /// <summary>
        /// Time it takes to upgrade
        /// </summary>
        [JsonProperty("hours")]
        public double Hours { get; set; }

        /// <summary>
        /// Base cost of coins it takes to do the upgrade
        /// </summary>
        [JsonProperty("cost")]
        public int Cost { get; set; }

        /// <summary>
        /// Material (if any) required to upgrade
        /// </summary>
        [JsonProperty("material")]
        public string Material { get; set; }

        /// <summary>
        /// Amount of <see cref="Material"/> required to do the upgrade
        /// </summary>
        [JsonProperty("amount")]
        public int Amount { get; set; }

        public string Material2 { get; set; }

        public int Amount2 { get; set; }
        public string Material3 { get; set; }
        public int Amount3 { get; set; }
        public string Material4 { get; set; }
        public int Amount4 { get; set; }

        /// <summary>
        /// Coflnet Item tag for the Pet
        /// </summary>
        public string ItemTag => "PET_" + Name.ToUpper();
    }
}
