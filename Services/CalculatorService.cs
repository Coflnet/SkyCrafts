using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Crafts.Services
{
    public class CalculatorService
    {
        private static readonly HttpClient client = new HttpClient();
        private IConfiguration config;

        public CalculatorService(IConfiguration config)
        {
            this.config = config;
        }

        public async Task<ProfitableCraft> GetCreaftingCost(string itemId, Dictionary<string, ProfitableCraft> crafts)
        {
            var item = JsonSerializer.Deserialize<ItemData>(File.ReadAllText($"itemData/items/{itemId}.json"));
            var ingredients = NeedCount(item).ToList();
            var sellPriceTask = GetPriceFor(itemId, 1);
            await Task.WhenAll(ingredients.Select(async item =>
            {
                try
                {
                    if (item.ItemId == "SKYBLOCK_COIN")
                    {
                        item.Cost = item.Count;
                        return;
                    }
                    PriceResponse prices = await GetPriceFor(item.ItemId, item.Count);
                    item.Cost = prices.BuyPrice;
                    if (prices.Available < item.Count)
                        item.Cost = int.MaxValue;
                    if(crafts.TryGetValue(item.ItemId, out ProfitableCraft craft))
                        item.Cost = Math.Min(item.Cost, craft.CraftCost * item.Count * 1.05);
                }
                catch (System.Net.Http.HttpRequestException)
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
                ItemName = item.displayname,
                SellPrice = (await sellPriceTask).SellPrice,
                Type = item.recipes?.FirstOrDefault()?.type
            };
        }

        private async Task<PriceResponse> GetPriceFor(string itemTag, int count)
        {
            var baseUrl = config["API_BASE_URL"];
            if (baseUrl == "https://sky.coflnet.com")
                await Task.Delay(600); // avoid rate limiting
            var url = $"{config["API_BASE_URL"]}/api/item/price/{System.Web.HttpUtility.UrlEncode(itemTag)}/current?count={count}";
            var response = await client.GetStringAsync(url);
            try
            {
                return JsonSerializer.Deserialize<PriceResponse>(response);
            }
            catch (System.Text.Json.JsonException)
            {
                Console.WriteLine($"FATAL: Requesting {url} failed with invalid response '{response}'");
                return new PriceResponse()
                {
                    Available = 0,
                    BuyPrice = int.MaxValue,
                    SellPrice = 0
                };
            }
        }

        public IEnumerable<Ingredient> NeedCount(ItemData item)
        {
            var aggregated = GetIngredientsFromSlots(item).GroupBy(i => i.ItemId).Select(i => new Ingredient()
            {
                ItemId = i.Key,
                Count = i.Sum(single => single.Count)
            });
            return aggregated;
        }
        private IEnumerable<Ingredient> GetIngredientsFromSlots(ItemData item)
        {
            foreach (var ingredient in item.GetIngredients())
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
