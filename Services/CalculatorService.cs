using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;

namespace Coflnet.Sky.Crafts.Services
{
    public class CalculatorService
    {
        private static readonly HttpClient client = new HttpClient();
        public async Task<ProfitableCraft> GetCreaftingCost(string itemId)
        {
            var ingredients = NeedCount(itemId).ToList();
            var sellPriceTask = GetPriceFor(itemId, 1);
            await Task.WhenAll(ingredients.Select(async item =>
            {
                try
                {
                    PriceResponse prices = await GetPriceFor(item.ItemId, item.Count);
                    item.Cost = prices.BuyPrice;
                    if(prices.Available < item.Count)
                        item.Cost = int.MaxValue;
                }
                catch (System.Net.Http.HttpRequestException e)
                {
                    // likely unobtainable
                    item.Cost = 0;
                }
            }).ToArray());
            return new ProfitableCraft()
            {
                CraftCost = ingredients.Sum(i => i.Cost),
                Ingredients = ingredients,
                ItemId = itemId,
                SellPrice = (await sellPriceTask).SellPrice
            };
        }

        private static async Task<PriceResponse> GetPriceFor(string itemTag, int count)
        {
            var response = await client.GetStringAsync($"https://sky.coflnet.com/api/item/price/{System.Web.HttpUtility.UrlEncode(itemTag)}/current?count={count}");
            var prices = JsonSerializer.Deserialize<PriceResponse>(response);
            return prices;
        }

        public IEnumerable<Ingredient> NeedCount(string itemId)
        {
            var aggregated = GetIngredientsFromSlots(itemId).GroupBy(i => i.ItemId).Select(i => new Ingredient()
            {
                ItemId = i.Key,
                Count = i.Sum(single => single.Count)
            });
            return aggregated;
        }
        private IEnumerable<Ingredient> GetIngredientsFromSlots(string itemId)
        {
            var item = JsonSerializer.Deserialize<ItemData>(File.ReadAllText($"itemData/items/{itemId}.json"));
            foreach (var ingredient in item.recipe.Values)
            {
                if (string.IsNullOrEmpty(ingredient))
                    continue;
                var parts = ingredient.Split(':');
                yield return new Ingredient()
                {
                    ItemId = ConvertNeuToTag(parts.First()),
                    Count = parts.Length == 1 ? 1 : int.Parse(parts[1])
                };
            }
        }

        public string ConvertNeuToTag(string neu)
        {
            if (neu.Contains(";"))
                return neu.Replace('-', ':');
            return neu;
        }
    }
}
