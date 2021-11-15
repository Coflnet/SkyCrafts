using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services
{
    public class UpdaterService : BackgroundService
    {
        private CraftingRecipeService craftingRecipeService;
        private CalculatorService calculatorService;
        private CollectionService collectionService;
        private ILogger<UpdaterService> logger;
        public Dictionary<string, ProfitableCraft> Crafts = new Dictionary<string, ProfitableCraft>();
        Prometheus.Counter profitableFound = Prometheus.Metrics.CreateCounter("sky_craft_profitable", "How many profitable items were found");

        public UpdaterService(CraftingRecipeService craftingRecipeService,
                    CalculatorService calculatorService,
                    ILogger<UpdaterService> logger,
                    CollectionService collectionService)
        {
            this.craftingRecipeService = craftingRecipeService;
            this.calculatorService = calculatorService;
            this.logger = logger;
            this.collectionService = collectionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var craftable = craftingRecipeService.CraftAbleItems().ToList();
            while (!stoppingToken.IsCancellationRequested)
                await IterateAll(craftable, stoppingToken);
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
                    if (result.CraftCost < result.SellPrice)
                    {
                        profitableFound.Inc();
                        if (result.CraftCost < result.SellPrice * 0.5)
                            logger.LogInformation("double " + result.ItemId);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "updating item " + item.internalname);
                }
            }
        }
    }
}
