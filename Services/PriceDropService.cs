using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Models;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services;

public class PriceDropService
{
    private readonly IPricesApi pricesApi;
    private readonly IItemApi itemsApi;
    private readonly ILogger<PriceDropService> logger;
    private readonly Dictionary<string, double> priceCache = new();
    private readonly Dictionary<string, DropStatistic> stats = new();
    public PriceDropService(IPricesApi pricesApi, ILogger<PriceDropService> logger, IItemApi itemsApi)
    {
        this.pricesApi = pricesApi;
        this.logger = logger;
        this.itemsApi = itemsApi;
    }
    public async Task UpdateAll(Dictionary<string, ProfitableCraft> crafts)
    {
        var all = await itemsApi.ApiItemsGetAsync();
        foreach (var item in all.Where(i => i.Flags.Value.HasFlag(Api.Client.Model.ItemFlags.BAZAAR) ||
            i.Flags.Value.HasFlag(Api.Client.Model.ItemFlags.AUCTION)))
        {
            try
            {
                await UpdatePrice(crafts, item);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Error while updating price for {item.Tag}");
            }
        }
    }

    private async Task UpdatePrice(Dictionary<string, ProfitableCraft> crafts, Api.Client.Model.ItemMetadataElement item)
    {
        var prices = await pricesApi.ApiItemPriceItemTagHistoryMonthGetAsync(item.Tag, new Dictionary<string, string>() { { "Clean", "true" } });
        if (prices.Count == 0)
        {
            logger.LogWarning($"No prices found for {item.Tag}");
            return;
        }
        var median = prices.Select(p => p.Avg).Where(p => p > 0).OrderBy(p => p).Skip(prices.Count / 2).First();
        priceCache[item.Tag] = median;
        if (crafts.TryGetValue(item.Tag, out var craft))
        {
            var current = craft.Median;
            stats[item.Tag] = new DropStatistic()
            {
                Monthly = median,
                Now = craft.SellPrice,
                Recent = current,
                Tag = item.Tag,
                Volume = craft.Volume,
                LastUpdated = DateTime.UtcNow
            };
        }
        else
        {
            var currentPrice = await pricesApi.ApiItemPriceItemTagGetAsync(item.Tag, new Dictionary<string, string>() { { "Clean", "true" } });
            var lbin = await pricesApi.ApiItemPriceItemTagCurrentGetAsync(item.Tag);
            if (currentPrice == null)
            {
                logger.LogWarning($"No current price found for {item.Tag}");
                return;
            }
            var current = currentPrice.Median;
            stats[item.Tag] = new DropStatistic()
            {
                Monthly = median,
                Recent = current,
                Now = lbin.Buy,
                Tag = item.Tag,
                Volume = currentPrice.Volume,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    public double GetMedian(string tag)
    {
        if (priceCache.TryGetValue(tag, out var price))
            return price;
        return 0;
    }

    internal IEnumerable<DropStatistic> GetAllDrops()
    {
        return stats.Values.OrderByDescending(s => s.Monthly).ThenByDescending(s => s.Recent).ThenByDescending(s => s.Volume);
    }

    public class DropStatistic
    {
        public double Monthly { get; set; }
        public double Recent { get; set; }
        public string Tag { get; set; }
        public double Volume { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public double Now { get; set; }
    }
}
