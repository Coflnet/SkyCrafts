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
    public class UpdaterService : BackgroundService
    {
        private CraftingRecipeService craftingRecipeService;
        private CalculatorService calculatorService;
        private CollectionService collectionService;
        private KatUpgradeService katService;
        private ILogger<UpdaterService> logger;
        public Dictionary<string, ProfitableCraft> Crafts = new Dictionary<string, ProfitableCraft>();
        public HashSet<string> BazaarItems = new();
        private IConfiguration config;
        public bool IteratedAll = false;
        Prometheus.Counter profitableFound = Prometheus.Metrics.CreateCounter("sky_craft_profitable", "How many profitable items were found");

        public UpdaterService(CraftingRecipeService craftingRecipeService,
                    CalculatorService calculatorService,
                    ILogger<UpdaterService> logger,
                    CollectionService collectionService, KatUpgradeService katService, IConfiguration config)
        {
            this.craftingRecipeService = craftingRecipeService;
            this.calculatorService = calculatorService;
            this.logger = logger;
            this.collectionService = collectionService;
            this.katService = katService;
            this.config = config;
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
            await Parallel.ForEachAsync(craftable, options,
            async (item, token) =>
            {
                if (stoppingToken.IsCancellationRequested)
                    return;
                if (item.internalname.Contains("GENERATOR"))
                    return; //skip minions
                if (item.internalname.Contains(";") && item.displayname != "§fEnchanted Book")
                    return; // skip level items (potions, pets)
                if (item.internalname.Contains("-"))
                    return; // skip minecraft type items (STEP-3, STAINED_GLASS-14 etc)
                if (item.internalname.EndsWith("_SACK"))
                    return; // not sellable
                try
                {
                    var result = await calculatorService.GetCreaftingCost(item.internalname, Crafts);
                    var tag = result.ItemId;

                    if (item.displayname == "§fEnchanted Book")
                    {
                        tag = "ENCHANTMENT_" + item.internalname.Replace(";", "_");
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
                    }
                    Crafts[tag] = result;
                    if (result.ReqCollection == default)
                    {
                        var name = Regex.Replace(item.displayname, @"§[\da-f]", "");
                        result.ReqCollection = await collectionService.GetRequiredCollection(name);
                    }
                    if (!string.IsNullOrEmpty(item.slayer_req))
                        result.ReqSlayer = new Models.RequiredCollection()
                        {
                            Name = item.slayer_req.Split("_").First(),
                            Level = int.Parse(item.slayer_req.Split("_").Last())
                        };
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
    }
}
