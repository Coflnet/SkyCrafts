using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services;

public class NpcBuyService(IItemsApi playerItemsApi, IItemApi apiItemsApi, IPricesApi pricesApi, ILogger<NpcBuyService> logger)
{
    private readonly IItemsApi playerItemsApi = playerItemsApi;
    private readonly IItemApi apiItemsApi = apiItemsApi;
    private readonly IPricesApi pricesApi = pricesApi;
    private readonly ConcurrentDictionary<string, ReverseNpcFlip> flips = new();
    private readonly ILogger<NpcBuyService> logger = logger;

    public async Task UpdatePrices()
    {
        if (flips.Count == 0)
            await LoadItems();
        foreach (var item in flips.ToList())
        {
            var generalPriceInfo = await pricesApi.ApiItemPriceItemTagGetAsync(item.Key);
            if (generalPriceInfo.Volume == 0 && generalPriceInfo.Max == 0)
            { // not sellable
                flips.TryRemove(item.Key, out _);
                continue;
            }
            var sellPrice = await pricesApi.ApiItemPriceItemTagCurrentGetAsync(item.Key);
            item.Value.SellPrice = Math.Min(sellPrice.Sell > 0 ? sellPrice.Sell : sellPrice.Buy, generalPriceInfo.Median * 2);
            foreach (var cost in item.Value.Costs)
            {
                if(cost.ItemTag == "SKYBLOCK_COINS")
                {
                    cost.Price = cost.Amount;
                    continue;
                }
                var costPrice = await pricesApi.ApiItemPriceItemTagCurrentGetAsync(cost.ItemTag, cost.Amount);
                cost.Price = costPrice.Sell > 0 ? costPrice.Sell : costPrice.Buy;
                if (cost.Price <= 0 && costPrice.Available <= 0)
                {
                    logger.LogWarning("Item {item} used in reverse npc flip {flip} has no valid price, removing flip", cost.ItemName, item.Value.ItemName);
                    flips.TryRemove(item.Key, out _);
                    break;
                }
            }
            var totalCost = item.Value.Costs.Sum(c => c.Price * c.Amount);
            item.Value.NpcBuyPrice = totalCost;
            item.Value.Profit = item.Value.SellPrice - totalCost;
            item.Value.ProfitMargin = totalCost > 0 ? item.Value.Profit / totalCost : 0;
            item.Value.Volume = generalPriceInfo.Volume;
            item.Value.LastUpdated = DateTime.UtcNow;
        }
    }

    internal IEnumerable<ReverseNpcFlip> GetReverseFlips()
    {
        return flips.Values.Where(f => f.Profit > 0).OrderByDescending(f => f.ProfitMargin);
    }

    private async Task LoadItems()
    {
        var allItems = await playerItemsApi.ApiItemsNpccostGetAsync();
        var names = await apiItemsApi.ApiItemsGetAsync();
        var lookup = names.GroupBy(n => n.Name).Select(g => g.First()).Where(e => e.Name != null).ToDictionary(n => n.Name, n => n.Tag);
        lookup.Add("Coins", "SKYBLOCK_COINS");
        var reverseLookup = names.ToDictionary(n => n.Tag, n => n.Name);
        foreach (var item in allItems.GroupBy(i=>i.ItemTag).Select(g=>g.OrderByDescending(i=>i.Stock).First()))
        {
            if (item.Description.Contains("Soulbound"))
                continue; // Can't sell soulbound items
            var flip = new ReverseNpcFlip()
            {
                NpcName = item.NpcName,
                ItemId = item.ItemTag,
                ItemName = reverseLookup.ContainsKey(item.ItemTag) ? reverseLookup[item.ItemTag] : item.ItemTag,
                Costs = item.Costs?.Select(i => new ReverseNpcFlip.Cost()
                {
                    ItemName = i.Key,
                    ItemTag = lookup.ContainsKey(i.Key) ? lookup[i.Key] : null,
                    Amount = i.Value
                }).ToList() ?? new List<ReverseNpcFlip.Cost>(),

            };
            if (flip.Costs.Any(e => e.ItemTag == null))
            {
                logger.LogWarning("Skipping reverse flip for {item} because some item tags could not be resolved {unknown}", item.ItemTag, string.Join(", ", flip.Costs.Where(c => c.ItemTag == null).Select(c => c.ItemName)));
                continue;
            }
            flips[item.ItemTag] = flip;
        }
    }
}
