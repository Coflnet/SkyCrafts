using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services;

public class NpcSellService
{
    private readonly IItemsApi itemsApi;
    private readonly IPricesApi pricesApi;
    private readonly ILogger<NpcSellService> logger;
    private readonly UpdaterService updaterService;
    private readonly GeorgePetOfferService georgePetOfferService;
    private readonly TimeSpan npcPriceCacheDuration = TimeSpan.FromHours(12);
    private readonly TimeSpan flipCacheDuration = TimeSpan.FromHours(3);
    private readonly SemaphoreSlim npcPriceSemaphore = new(1, 1);
    private readonly SemaphoreSlim flipSemaphore = new(1, 1);
    private IReadOnlyDictionary<string, string> cachedItemNames = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, double> cachedNpcSellPrices = new Dictionary<string, double>();
    private DateTime npcPricesLastFetched = DateTime.MinValue;
    private IReadOnlyCollection<NpcFlip> cachedFlips = Array.Empty<NpcFlip>();
    private DateTime flipsLastUpdated = DateTime.MinValue;

    public NpcSellService(IItemsApi itemsApi, IPricesApi pricesApi, UpdaterService updaterService, GeorgePetOfferService georgePetOfferService, ILogger<NpcSellService> logger)
    {
        this.itemsApi = itemsApi;
        this.pricesApi = pricesApi;
        this.updaterService = updaterService;
        this.georgePetOfferService = georgePetOfferService;
        this.logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, double>> GetNpcSellPrices(bool forceRefresh = false)
    {
        if (!forceRefresh && cachedNpcSellPrices.Count > 0 && DateTime.UtcNow - npcPricesLastFetched < npcPriceCacheDuration)
            return cachedNpcSellPrices;

        await npcPriceSemaphore.WaitAsync();
        try
        {
            if (!forceRefresh && cachedNpcSellPrices.Count > 0 && DateTime.UtcNow - npcPricesLastFetched < npcPriceCacheDuration)
                return cachedNpcSellPrices;

            var npcSellPrices = new Dictionary<string, double>();
            var metadataLookup = new Dictionary<string, string>();
            try
            {
                var items = await itemsApi.ItemsGetAsync();
                var itemList = items?.Where(i => i != null).ToList() ?? new List<Item>();
                var georgeSnapshot = await georgePetOfferService.GetSnapshotAsync(itemList, forceRefresh);

                foreach (var item in itemList)
                {
                    if (item?.Tag == null)
                        continue;

                    double price = item.NpcSellPrice;
                    if (georgeSnapshot.TryGetPrice(item.Tag, out var georgePrice))
                        price = georgePrice;

                    if (price <= 0)
                        continue;

                    npcSellPrices[item.Tag] = price;
                    metadataLookup[item.Tag] = ResolveItemName(item, item.Tag);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while fetching NPC sell prices");
                if (cachedNpcSellPrices.Count > 0)
                    return cachedNpcSellPrices; // fall back to stale data
                throw;
            }

            cachedNpcSellPrices = npcSellPrices;
            cachedItemNames = metadataLookup;
            npcPricesLastFetched = DateTime.UtcNow;
            return cachedNpcSellPrices;
        }
        finally
        {
            npcPriceSemaphore.Release();
        }
    }

    public async Task<IReadOnlyCollection<NpcFlip>> GetNpcFlipOpportunities(bool forceRefresh = false)
    {
        if (!forceRefresh && cachedFlips.Count > 0 && DateTime.UtcNow - flipsLastUpdated < flipCacheDuration)
            return cachedFlips;

        await flipSemaphore.WaitAsync();
        try
        {
            if (!forceRefresh && cachedFlips.Count > 0 && DateTime.UtcNow - flipsLastUpdated < flipCacheDuration)
                return cachedFlips;

            var npcSellPrices = await GetNpcSellPrices(forceRefresh);
            if (npcSellPrices.Count == 0)
            {
                cachedFlips = Array.Empty<NpcFlip>();
                flipsLastUpdated = DateTime.UtcNow;
                return cachedFlips;
            }

            var flips = new ConcurrentBag<NpcFlip>();
            var semaphore = new SemaphoreSlim(12);
            var tasks = new List<Task>();

            foreach (var kv in npcSellPrices)
            {
                var tag = kv.Key;
                var npcSellPrice = kv.Value;
                tasks.Add(ProcessItem(tag, npcSellPrice));
            }

            await Task.WhenAll(tasks);

            cachedFlips = flips
                .OrderByDescending(f => f.Profit)
                .ThenByDescending(f => f.ProfitMargin)
                .ToArray();
            flipsLastUpdated = DateTime.UtcNow;
            return cachedFlips;

            async Task ProcessItem(string tag, double npcSellPrice)
            {
                await semaphore.WaitAsync();
                try
                {
                    double buyPrice = 0;
                    var price = await pricesApi.ApiItemPriceItemTagCurrentGetAsync(tag);
                    if (tag.Contains("PERFECT_"))
                        logger.LogDebug("Fetched current price for {tag}: {@price} available: {available}", tag, price, price?.Available ?? 0);
                    var available = price?.Available ?? 0;
                    if (available > 0)
                        buyPrice = price?.Buy ?? 0;

                    if (buyPrice <= 0 || buyPrice >= npcSellPrice - 0.1)
                    {
                        if (tag.Contains("PERFECT_"))
                            logger.LogDebug("Skipping npc flip for {tag} because buy price is {buyPrice} and npc sell price is {npcSellPrice}", tag, buyPrice, npcSellPrice);
                        return;
                    }
                    var profit = npcSellPrice - buyPrice;
                    var margin = buyPrice > 0 ? profit / buyPrice : 0;
                    cachedItemNames.TryGetValue(tag, out var displayName);
                    flips.Add(new NpcFlip
                    {
                        ItemId = tag,
                        ItemName = displayName ?? tag,
                        BuyPrice = buyPrice,
                        NpcSellPrice = npcSellPrice,
                        Profit = profit,
                        ProfitMargin = margin,
                        LastUpdated = DateTime.UtcNow
                    });
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Failed to evaluate npc flip for {tag}", tag);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }
        finally
        {
            flipSemaphore.Release();
        }
    }

    private static string ResolveItemName(Item item, string fallback)
    {
        if (item == null)
            return fallback;
        foreach (var propertyName in new[] { "DisplayName", "Name", "Tag" })
        {
            var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.GetValue(item) is string value && !string.IsNullOrEmpty(value))
                return value;
        }
        return fallback;
    }
}
