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
            foreach (var item in craftable)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;
                if (item.internalname.Contains("GENERATOR"))
                    continue; //skip minions
                if (item.internalname.Contains(";"))
                    continue; // skip level items (potions, pets)
                if (item.internalname.Contains("-"))
                    continue; // skip minecraft type items (STEP-3, STAINED_GLASS-14 etc)
                if (item.internalname.EndsWith("_SACK"))
                    continue; // not sellable
                try
                {
                    var result = await calculatorService.GetCreaftingCost(item.internalname);
                    Crafts[result.ItemId] = result;
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
            }
            IteratedAll = true;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}
