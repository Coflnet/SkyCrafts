using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Crafts.Services
{
    public partial class UpdaterService : BackgroundService
    {
        private CraftingRecipeService craftingRecipeService;
        private CalculatorService calculatorService;
        private CollectionService collectionService;
        private KatUpgradeService katService;
        private Api.Client.Api.IPricesApi pricesApi;
        private ILogger<UpdaterService> logger;
        public Dictionary<string, ProfitableCraft> Crafts = new Dictionary<string, ProfitableCraft>();
        public HashSet<string> BazaarItems = new();
        private IConfiguration config;
        public bool IteratedAll = false;
        Prometheus.Counter profitableFound = Prometheus.Metrics.CreateCounter("sky_craft_profitable", "How many profitable items were found");

        public UpdaterService(CraftingRecipeService craftingRecipeService,
                    CalculatorService calculatorService,
                    ILogger<UpdaterService> logger,
                    CollectionService collectionService, KatUpgradeService katService, IConfiguration config, Api.Client.Api.IPricesApi pricesApi)
        {
            this.craftingRecipeService = craftingRecipeService;
            this.calculatorService = calculatorService;
            this.logger = logger;
            this.collectionService = collectionService;
            this.katService = katService;
            this.config = config;
            this.pricesApi = pricesApi;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var getBazaarItemsTask = GetBazaarItems();
            var craftable = craftingRecipeService.CraftAbleItems().ToList();
            await getBazaarItemsTask;
            while (!stoppingToken.IsCancellationRequested)
            {
                await katService.Update();
                await IterateAll(craftable, stoppingToken);
            }
        }

        private async Task GetBazaarItems()
        {
            var fullUrl = config["API_BASE_URL"] + "/api/items/bazaar/tags";
            for (int i = 0; i < 5; i++)
                try
                {
                    var client = new HttpClient();
                    var data = await client.GetStringAsync(fullUrl);
                    BazaarItems = JsonConvert.DeserializeObject<HashSet<string>>(data);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to retrieve bazaar items from " + fullUrl);
                    if (i >= 4)
                        throw;
                }
        }

        private async Task IterateAll(List<ItemData> craftable, CancellationToken stoppingToken)
        {
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = IteratedAll ? 1 : 3,
                CancellationToken = stoppingToken
            };
            var lookup = craftable.GroupBy(c => c.internalname).Select(c => c.First()).ToDictionary(c => c.internalname);
            await Parallel.ForEachAsync(craftable, options,
            async (item, token) =>
            {
                if (stoppingToken.IsCancellationRequested)
                    return;
                if (item.internalname.Contains("GENERATOR"))
                    return; //skip minions
                if (item.internalname.Contains(";") && item.displayname != "§fEnchanted Book")
                    return; // skip level items (potions, pets)
                            // if (item.internalname.Contains("-"))
                            //    return; // skip minecraft type items (STEP-3, STAINED_GLASS-14 etc)
                if (item.internalname.EndsWith("_SACK"))
                    return; // not sellable
                if (item.internalname.EndsWith("POTION"))
                    return; // too different
                try
                {
                    var result = await calculatorService.GetCreaftingCost(item, Crafts, lookup);
                    var tag = result.ItemId;

                    if (item.displayname == "§fEnchanted Book")
                    {
                        tag = CorrectEnchantTagAndAddLvl5(item, result);
                    }
                    await TryAddmedianAndVolume(result, tag);
                    Crafts[tag] = result;
                    await AssignRequirements(item, result);
                    if (result.CraftCost < result.SellPrice)
                    {
                        profitableFound.Inc();
                        if (result.CraftCost < result.SellPrice * 0.5)
                            logger.LogInformation("double " + result.ItemId);
                    }
                    if (IteratedAll)
                        await Task.Delay(50);
                }
                catch (Exception e)
                {
                    if (e.Message.Contains("Too Many Requests"))
                        logger.LogInformation("sent to many requests");
                    else
                        logger.LogError(e, "updating item " + item.internalname);
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            });
            if (!IteratedAll)
                Console.WriteLine("Finished first iteration, am now ready");
            IteratedAll = true;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        private async Task AssignRequirements(ItemData item, ProfitableCraft result)
        {
            if (result.ReqCollection == default)
            {
                var name = MinecraftFormatRemoveRegex().Replace(item.displayname, "");
                result.ReqCollection = await collectionService.GetRequiredCollection(name);
            }
            if (!string.IsNullOrEmpty(item.slayer_req))
                result.ReqSlayer = new Models.RequiredCollection()
                {
                    Name = item.slayer_req.Split("_").First(),
                    Level = int.Parse(item.slayer_req.Split("_").Last())
                };
            else
            {
                var SlayerLine = item.lore?.Where(l => l.Contains("Slayer ")).FirstOrDefault();
                if (SlayerLine != null)
                {
                    var match = Regex.Match(MinecraftFormatRemoveRegex().Replace(SlayerLine, ""), @"☠ Requires (.*) Slayer (\d)");
                    if (match.Success)
                    {
                        result.ReqSlayer = new RequiredCollection()
                        {
                            Name = match.Groups[1].Value,
                            Level = int.Parse(match.Groups[2].Value)
                        };
                    }

                }
            }
        }

        private async Task TryAddmedianAndVolume(ProfitableCraft result, string tag)
        {
            if (tag.Contains("-"))
                return; // skip minecraft type items (STEP-3, STAINED_GLASS-14 etc)
            if (tag.Contains(":"))
            {
                if (!IteratedAll)
                    return; // not relevant on first iteration
                tag = tag.Split(":").First();
            }
            if ((!Crafts.TryGetValue(tag, out ProfitableCraft existing) || existing.CraftCost != result.CraftCost)
                && result.CraftCost < int.MaxValue && !result.ItemId.StartsWith("ENCHANTMENT_"))
            {
                // update volume and median
                try
                {

                    var prices = await pricesApi.ApiItemPriceItemTagGetAsync(tag, new Dictionary<string, string>() { { "Clean", "true" } });
                    if (prices != null)
                    {
                        result.Volume = prices.Volume;
                        result.Median = prices.Median;
                        result.SellPrice = Math.Min(result.SellPrice, prices.Median * 2);
                        logger.LogInformation("Updated price data for " + tag + " " + result.Volume + " " + result.Median);
                    }
                    else
                    {
                        logger.LogInformation("No price data for " + tag);
                    }
                }
                catch (System.Exception)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(result));
                    throw;
                }
            }
        }

        private string CorrectEnchantTagAndAddLvl5(ItemData item, ProfitableCraft result)
        {
            string tag = "ENCHANTMENT_" + item.internalname.Replace(";", "_");
            result.ItemId = tag;
            result.ItemName = item.lore.FirstOrDefault() ?? item.displayname;
            // scale up to lvl 5
            var baselevel = int.Parse(tag.Split("_").Last());
            var lvl5Tag = tag.Replace(baselevel.ToString(), "5");
            var booksRequired = (int)Math.Pow(2, 5 - baselevel);
            var adjustedName = (item.lore.FirstOrDefault() ?? item.displayname);

            var lvl5Result = new ProfitableCraft()
            {
                Ingredients = new List<Ingredient>(){new(){
                                Cost = result.CraftCost * booksRequired,
                                Count = booksRequired,
                                ItemId = tag
                            }},
                CraftCost = result.CraftCost * booksRequired,
                ItemId = lvl5Tag,
                ItemName = adjustedName.Substring(0, adjustedName.LastIndexOf(' ')) + " V",
                SellPrice = result.SellPrice,
                Type = result.Type
            };
            if (lvl5Result != null)
                Crafts[lvl5Tag] = lvl5Result;
            else
                logger.LogInformation("lvl5Result is null for " + tag);
            return tag;
        }

        [GeneratedRegex(@"§[\da-f]")]
        private static partial Regex MinecraftFormatRemoveRegex();
    }
}
