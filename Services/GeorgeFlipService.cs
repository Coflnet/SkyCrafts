using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Crafts.Models;
using Api = Coflnet.Sky.Api.Client;
using ApiModel = Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Api.Client.Api;

namespace Coflnet.Sky.Crafts.Services;

public class GeorgeFlipService
{
    private readonly IConfiguration config;
    private readonly GeorgePetOfferService georgePetOfferService;
    private readonly ILogger<GeorgeFlipService> logger;
    private readonly TimeSpan cacheDuration = TimeSpan.FromHours(3);
    private DateTime lastUpdated = DateTime.MinValue;
    private List<GeorgeFlipResult> results = new();
    private readonly AuctionsApi auctionsApi;

    public GeorgeFlipService(IConfiguration config, GeorgePetOfferService georgePetOfferService, ILogger<GeorgeFlipService> logger)
    {
        this.config = config;
        this.georgePetOfferService = georgePetOfferService;
        this.logger = logger;
        auctionsApi = new(config["API_BASE_URL"]);
    }

    public IReadOnlyList<GeorgeFlipResult> Results => results;

    public async Task Update(bool force = false)
    {
        if (!force && DateTime.UtcNow - lastUpdated < cacheDuration)
            return;

        var snapshot = await georgePetOfferService.GetSnapshotAsync(false);
        var list = new List<GeorgeFlipResult>();
        var offers = snapshot.OffersByTag.Values.ToList();
        logger.LogInformation("Starting George flip scan for {count} offers", offers.Count);

        foreach (var offer in offers)
        {
            try
            {
                var baseItemTag = "PET_" + offer.Key.BaseTag;
                // Query active bins for this base tag and rarity
                var auctions = await GetActiveBins(baseItemTag, (ApiModel.Tier)offer.Key.Rarity);
                if (auctions == null || auctions.Count == 0)
                    continue;

                // Use the first returned auction as reference LBIN (API returns sorted best first)
                var lbin = auctions.First();
                foreach (var auction in auctions)
                {
                    // compute profit using static george price
                    var profit = offer.Price - auction.StartingBid;
                    if (profit <= 0)
                        continue;

                    var margin = auction.StartingBid > 0 ? profit / auction.StartingBid : 0;
                    list.Add(new GeorgeFlipResult
                    {
                        ItemTag = offer.ItemTag,
                        ItemName = auction.ItemName,
                        Rarity = (ApiModel.Tier)offer.Key.Rarity,
                        OriginAuction = auction.Uuid,
                        ReferenceAuction = lbin.Uuid,
                        PurchaseCost = auction.StartingBid,
                        TargetPrice = offer.Price,
                        Profit = profit,
                        ProfitMargin = margin,
                        LastUpdated = DateTime.UtcNow
                    });
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Failed evaluating George flip for {tag}", offer.ItemTag);
            }
        }

        // keep results sorted by profit
        results = list.OrderByDescending(r => r.Profit).ThenByDescending(r => r.ProfitMargin).ToList();
        lastUpdated = DateTime.UtcNow;
        logger.LogInformation("George flip scan done, found {count} flips", results.Count);
    }

    private static readonly HttpClient client = new();
    private async Task<T> GetFromApi<T>(string path) where T : class
    {
        var baseUrl = config["API_BASE_URL"];
        if (baseUrl == "https://sky.coflnet.com")
            await Task.Delay(500); // avoid rate limit
        var manual = await client.GetStringAsync($"{baseUrl}{path}");
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(manual);
    }

    private async Task<List<Api.Client.Model.SaveAuction>> GetActiveBins(string itemTag, Api.Client.Model.Tier rarity)
    {
        return await GetFromApi<List<Api.Client.Model.SaveAuction>>($"/api/auctions/tag/{itemTag}/active/bin?Rarity={rarity}");
    }
}
