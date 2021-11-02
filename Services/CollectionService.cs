using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;

namespace Coflnet.Sky.Crafts.Services
{
    public class CollectionService
    {
        private Dictionary<string, RequiredCollection> RequiredCollection = new Dictionary<string, RequiredCollection>();
        public CollectionService()
        {

        }

        public async Task<RequiredCollection> GetRequiredCollection(string itemName)
        {
            await EnsureDataIsLoaded();
            if (RequiredCollection.TryGetValue(itemName, out RequiredCollection value))
                return value;
            return default;
        }

        private async Task EnsureDataIsLoaded()
        {
            // is already loaded
            if (RequiredCollection.Count > 2)
                return;
            var amILoading = false;
            lock (RequiredCollection)
            {
                if (RequiredCollection.Count == 0)
                {
                    RequiredCollection["____mock___"] = null;
                    amILoading = true;
                }
            }
            if (!amILoading)
            {
                // wait for 10 seconds for first loader to load and then reload
                for (int i = 0; i < 10; i++)
                {
                    if (RequiredCollection.Count > 1)
                        return;
                    await Task.Delay(1000);
                }
            }
            var client = new HttpClient();
            var response = await client.GetStringAsync("https://api.hypixel.net/resources/skyblock/collections");
            var result = JsonSerializer.Deserialize<CollectionsRoot>(response);

            Console.WriteLine(result.LastUpdated);
            Console.WriteLine(result.Collections.Count);
            foreach (var category in result.Collections)
            {
                foreach (var collection in category.Value.Items)
                {
                    foreach (var tier in collection.Value.Tiers)
                    {
                        foreach (var item in tier.Unlocks.Where(i => i.Contains("Recipe")))
                        {
                            RequiredCollection[item.Replace(" Recipe", "")] = new RequiredCollection()
                            {
                                Name = collection.Key,
                                Level = tier.TierId
                            };
                        }
                    }
                }
            }
        }
    }
}
