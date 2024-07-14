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

        public async Task<ProfitableCraft> GetCreaftingCost(ItemData item, Dictionary<string, ProfitableCraft> crafts, Dictionary<string, ItemData> lookup)
        {
            //var item = JsonSerializer.Deserialize<ItemData>(File.ReadAllText($"itemData/items/{itemId}.json"));
            var ingredients = NeedCount(item).ToList();
            var sellPriceTask = GetPriceFor(item.internalname, 1);
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
                        item.Cost = 20_000_000_000;
                    var canBeCrafteDirectly = CanBeCraftedDirectly(lookup, item);
                    if (crafts.TryGetValue(item.ItemId, out ProfitableCraft craft))
                    {
                        var craftWithProfit = craft.CraftCost * item.Count * 1.12;
                        item.CraftCost = craft.CraftCost * item.Count;
                        if (!canBeCrafteDirectly)
                            craftWithProfit *= 100;
                        if (item.Cost > craftWithProfit)
                        {
                            item.Cost = Math.Min(item.Cost, craftWithProfit);
                            item.Type = "craft";
                        }
                    }
                }
                catch (HttpRequestException)
                {
                    // likely unobtainable
                    item.Cost = 0;
                }
            }).ToArray());
            return new ProfitableCraft()
            {
                CraftCost = ingredients.Sum(i => i.Cost),
                Ingredients = ingredients,
                ItemId = item.internalname,
                ItemName = item.displayname,
                SellPrice = (await sellPriceTask).SellPrice,
                Type = item.recipes?.FirstOrDefault()?.type
            };
        }

        private static bool CanBeCraftedDirectly(Dictionary<string, ItemData> lookup, Ingredient item)
        {
            return lookup.TryGetValue(item.ItemId, out ItemData itemData) && itemData.Type == null && (itemData.recipes == null || itemData.recipes.All(r => r.type != "forge" && r.type != "npc_shop"));
        }

        private async Task<PriceResponse> GetPriceFor(string itemTag, long count)
        {
            var baseUrl = config["API_BASE_URL"];
            if (baseUrl == "https://sky.coflnet.com")
                await Task.Delay(600); // avoid rate limiting
            var url = $"{config["API_BASE_URL"]}/api/item/price/{System.Web.HttpUtility.UrlEncode(itemTag)}/current?count={count}";
            var response = await client.GetStringAsync(url);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine($"FATAL: Requesting {url} failed with empty response");
                await Task.Delay(1000);
                response = await client.GetStringAsync(url);
            }
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
                    BuyPrice = 10_000_000_000,
                    SellPrice = 0
                };
            }
        }

        public IEnumerable<Ingredient> NeedCount(ItemData item)
        {
            var aggregated = GetIngredientsFromSlots(item).GroupBy(i => i.ItemId).Select(i => new Ingredient()
            {
                ItemId = i.Key,
                Count = i.Sum(single => single.Count),
                Type = i.First().Type
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
                    Count = parts.Length == 1 ? 1 : (int)float.Parse(parts[1])
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
