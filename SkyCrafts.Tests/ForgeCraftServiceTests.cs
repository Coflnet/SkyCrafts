using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using NpcCost = Coflnet.Sky.PlayerState.Client.Model.NpcCost;

namespace SkyCrafts.Tests;

public class ForgeCraftServiceTests
{
    private sealed class PricedCalculator(IConfiguration config, IItemsApi itemsApi, double platePrice) : CalculatorService(config, itemsApi)
    {
        protected override Task<PriceResponse> GetPriceFor(string itemTag, long count)
            => Task.FromResult(new PriceResponse
            {
                BuyPrice = (itemTag == "RAW" ? 10 : itemTag == "MITHRIL_PLATE" ? platePrice : 1_000_000) * count,
                SellPrice = 2_000_000,
                Available = (int)count,
                IsAh = true
            });
    }

    [Theory]
    [InlineData(1_000_000, 5 * (64_800 + 21_600) + 3 * 28_800)]
    [InlineData(1, 0)]
    public async Task DrillFlipAndAcquisitionPlan_IncludeOnlySelectedNestedForgeBatches(double platePrice, long subcraftDuration)
    {
        var config = Substitute.For<IConfiguration>();
        var itemsApi = Substitute.For<IItemsApi>();
        itemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var calculator = new PricedCalculator(config, itemsApi, platePrice);
        var drill = ForgeItem("TITANIUM_DRILL_2", 30, "MITHRIL_PLATE:5");
        var plate = ForgeItem("MITHRIL_PLATE", 64_800, "GOLDEN_PLATE:1");
        var golden = ForgeItem("GOLDEN_PLATE", 21_600, "INSTANT_PART:1");
        var diamond = ForgeItem("REFINED_DIAMOND", 28_800, "RAW:1");
        diamond.recipes[0].count = 2; // Synthetic batch yield verifies rounding at a deeper level.
        var instant = new ItemData { internalname = "INSTANT_PART", recipe = new Recipe { A1 = "REFINED_DIAMOND:1", count = 1 } };
        var items = new List<ItemData> { drill, plate, golden, instant, diamond };
        var lookup = items.ToDictionary(i => i.internalname);
        var craft = await calculator.GetCreaftingCost(drill, new(), lookup, new());
        var plan = await calculator.GetAcquisitionPlanAsync(drill.internalname, 1, lookup, new(), forceCraft: true);
        var forge = new ForgeCraftService(config, NullLogger<ForgeCraftService>.Instance);

        await forge.Update(new() { [drill.internalname] = craft }, items);

        var flip = Assert.Single(forge.FlipList);
        Assert.Equal(subcraftDuration, Assert.Single(craft.Ingredients).ForgeDuration);
        Assert.Equal(30 + subcraftDuration, flip.Duration);
        Assert.Equal(flip.Duration, plan.ForgeDuration);
        Assert.Equal((craft.SellPrice - craft.CraftCost) / Math.Max(flip.Duration + 100, 300) * 3600, flip.ProfitPerHour, 6);
    }

    private static ItemData ForgeItem(string tag, int duration, string input) => new()
    {
        internalname = tag,
        displayname = tag,
        recipes = new() { new NewRecipe { type = "forge", duration = duration, count = 1, inputs = new() { input } } }
    };
}
