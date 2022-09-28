using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Api.Client.Api;
using Newtonsoft.Json;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services
{
    public class KatUpgradeService
    {
        public List<KatUpgradeResult> Results = new();
        private IConfiguration config;
        private AuctionsApi auctionsApi;
        private PricesApi pricesApi;
        private ILogger<KatUpgradeService> logger;

        public KatUpgradeService(IConfiguration config, ILogger<KatUpgradeService> logger)
        {
            this.config = config;
            auctionsApi = new(config["API_BASE_URL"]);
            pricesApi = new(config["API_BASE_URL"]);
            this.logger = logger;
        }

        private static readonly HttpClient client = new();
        public async Task Update()
        {
            var list = await GetKatUpgradeCosts();
            logger.LogInformation("start updating kat list");
            var result = new List<KatUpgradeResult>();
            // List should be filled as fast as possible but not cleared after that
            if (Results.Count == 0)
                Results = result;
            foreach (var item in list)
            {
                try
                {
                    await CalculateKatFlips(result, item);
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Caclulating Kat flip {item.Name} {item.BaseRarity}");
                    await Task.Delay(1000);
                }
                await Task.Delay(100);
            }
            logger.LogInformation("done updating kat list");
            Results = result;
        }

        private async Task CalculateKatFlips(List<KatUpgradeResult> result, KatUpgradeCost item)
        {
            var rarity = item.BaseRarity;
            var upgradedRarity = (Api.Client.Model.Tier)((int)rarity + 1);
            var auctionsTask = GetActiveBins(item.ItemTag, rarity);
            var higherAuctions = await GetActiveBins(item.ItemTag, upgradedRarity);
            var auctions = await auctionsTask;
            if (auctions.Count == 0 || higherAuctions.Count == 0)
                return;
            var lbin = higherAuctions.First();
            var materialCost = await MaterialCost(item.Material, item.Amount);
            foreach (var auction in auctions)
            {
                var singleResult = SingleResult(item, auction, lbin, materialCost);
                if (singleResult != null)
                    result.Add(singleResult);
            }
        }

        private static KatUpgradeResult SingleResult(KatUpgradeCost item, Api.Client.Model.SaveAuction auction, Api.Client.Model.SaveAuction lbin, double materialCost)
        {
            var level = (float)int.Parse(Regex.Replace(auction.ItemName.Substring(0, 10), "[^0-9]", ""));
            var upgradeCost = item.Cost * (1 - (level - 1) * 0.003);
            var profit = lbin.StartingBid - auction.StartingBid - upgradeCost - materialCost;
            if (profit < 0)
                return null;
            return new KatUpgradeResult()
            {
                OriginAuction = auction.Uuid,
                CoreData = item,
                MaterialCost = materialCost,
                UpgradeCost = upgradeCost,
                Profit = profit,
                TargetRarity = (Api.Client.Model.Tier)lbin.Tier,
                ReferenceAuction = lbin.Uuid,
                PurchaseCost = auction.StartingBid,
                OriginAuctionName = auction.ItemName
            };
        }

        private async Task<List<Api.Client.Model.SaveAuction>> GetActiveBins(string itemTag, Api.Client.Model.Tier rarity)
        {
            var manual = await client.GetStringAsync($"{config["API_BASE_URL"]}/api/auctions/tag/{itemTag}/active/bin?Rarity={rarity}");
            return JsonConvert.DeserializeObject<List<Api.Client.Model.SaveAuction>>(manual);
        }

        private async Task<double> MaterialCost(string itemTag, int count)
        {
            if (itemTag == null)
                return 0;
            var manual = await client.GetStringAsync($"{config["API_BASE_URL"]}/api/item/price/{itemTag}/current?count={count}");
            var response = JsonConvert.DeserializeObject<Api.Client.Model.CurrentPrice>(manual);
            if (response.Available < count)
                return int.MaxValue;
            return response.Buy;
        }

        public async Task<IEnumerable<KatUpgradeCost>> GetKatUpgradeCosts()
        {
            var path = $"Data/KatUpgrade.json";
            return JsonConvert.DeserializeObject<IEnumerable<KatUpgradeCost>>(await File.ReadAllTextAsync(path));
        }
    }
}
