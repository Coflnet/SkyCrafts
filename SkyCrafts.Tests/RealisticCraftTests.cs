using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Xunit;

namespace SkyCrafts.Tests;

public class SmartBuyerTests
{
    [Fact]
    public void FillsCheapestTrancheFirst()
    {
        var tranches = new[]
        {
            new PriceTranche(50, 1_000_000, "insta"),
            new PriceTranche(30, 640, "npc"),
        };
        var (filled, unmet, dominant) = SmartBuyer.Cost(tranches, 1000);
        // 640 from npc @30, 360 from insta @50
        Assert.Equal(640 * 30 + 360 * 50, filled);
        Assert.Equal(0, unmet);
        Assert.Equal("npc", dominant);
    }

    [Fact]
    public void CapsEachTrancheAtItsCapacity()
    {
        var tranches = new[]
        {
            new PriceTranche(30, 640, "npc"),
            new PriceTranche(10, 100, "order"),
            new PriceTranche(20, 500, "insta"),
        };
        var (filled, unmet, _) = SmartBuyer.Cost(tranches, 900);
        // 100 @10 (order) + 500 @20 (insta) + 300 @30 (npc)
        Assert.Equal(100 * 10 + 500 * 20 + 300 * 30, filled);
        Assert.Equal(0, unmet);
    }

    [Fact]
    public void ReportsUnmetWhenSupplyExhausted()
    {
        var tranches = new[] { new PriceTranche(30, 640, "npc") };
        var (filled, unmet, _) = SmartBuyer.Cost(tranches, 1000);
        Assert.Equal(640 * 30, filled);
        Assert.Equal(360, unmet);
    }

    [Fact]
    public void ZeroQuantityIsFree()
    {
        var (filled, unmet, _) = SmartBuyer.Cost(new[] { new PriceTranche(30, 640, "npc") }, 0);
        Assert.Equal(0, filled);
        Assert.Equal(0, unmet);
    }

    [Fact]
    public void SummarizeTranches_NpcOrderAndInsta_ReportsBoundedWeightedBuckets()
    {
        var tranches = new[]
        {
            new PriceTranche(30, 640, "npc"),
            new PriceTranche(35, 60, "npc"),    // second npc tranche, weighted into the npc bucket
            new PriceTranche(40, 100, "order"),
            new PriceTranche(60, 500, "insta"),
            new PriceTranche(70, 500, "insta"), // included in the capacity-weighted average
        };

        var (npcCapacity, npcUnitPrice, orderCapacity, orderUnitPrice, instaCapacity, instaUnitPrice) = SmartBuyer.SummarizeTranches(tranches);

        Assert.Equal(640 + 60, npcCapacity);
        Assert.Equal((30d * 640 + 35d * 60) / (640 + 60), npcUnitPrice, 6);
        Assert.Equal(100, orderCapacity);
        Assert.Equal(40d, orderUnitPrice, 6);
        Assert.Equal(1000, instaCapacity);
        Assert.Equal(65d, instaUnitPrice);
    }

    [Fact]
    public void SummarizeTranches_OnlyInsta_ReportsBoundedWeightedInsta()
    {
        var tranches = new[]
        {
            new PriceTranche(80, 500, "insta"),
            new PriceTranche(60, 500, "insta"),
        };

        var (npcCapacity, npcUnitPrice, orderCapacity, orderUnitPrice, instaCapacity, instaUnitPrice) = SmartBuyer.SummarizeTranches(tranches);

        Assert.Equal(0, npcCapacity);
        Assert.Equal(0d, npcUnitPrice);
        Assert.Equal(0, orderCapacity);
        Assert.Equal(0d, orderUnitPrice);
        Assert.Equal(1000, instaCapacity);
        Assert.Equal(70d, instaUnitPrice);
    }

    [Fact]
    public void SummarizeTranches_OnlyNpcAndOrder_ReportsZeroInstaPrice()
    {
        var tranches = new[]
        {
            new PriceTranche(30, 640, "npc"),
            new PriceTranche(40, 100, "order"),
        };

        var (npcCapacity, npcUnitPrice, orderCapacity, orderUnitPrice, instaCapacity, instaUnitPrice) = SmartBuyer.SummarizeTranches(tranches);

        Assert.Equal(640, npcCapacity);
        Assert.Equal(30d, npcUnitPrice, 6);
        Assert.Equal(100, orderCapacity);
        Assert.Equal(40d, orderUnitPrice, 6);
        Assert.Equal(0, instaCapacity);
        Assert.Equal(0d, instaUnitPrice);
    }

    [Fact]
    public void SummarizeTranches_IgnoresZeroCapacityAndNegativePriceTranches()
    {
        var tranches = new[]
        {
            new PriceTranche(30, 0, "npc"),      // zero capacity - excluded
            new PriceTranche(-5, 100, "order"),  // negative price - excluded
            new PriceTranche(40, 100, "order"),
        };

        var (npcCapacity, npcUnitPrice, orderCapacity, orderUnitPrice, instaCapacity, instaUnitPrice) = SmartBuyer.SummarizeTranches(tranches);

        Assert.Equal(0, npcCapacity);
        Assert.Equal(0d, npcUnitPrice);
        Assert.Equal(100, orderCapacity);
        Assert.Equal(40d, orderUnitPrice);
        Assert.Equal(0, instaCapacity);
        Assert.Equal(0d, instaUnitPrice);
    }
}

public class RealisticCraftTests
{
    private class FakeMarket : IMarketSource
    {
        public Dictionary<string, List<PriceTranche>> Tranches { get; } = new();
        public Task<IReadOnlyList<PriceTranche>> GetBuyTranchesAsync(string tag)
            => Task.FromResult<IReadOnlyList<PriceTranche>>(Tranches.TryGetValue(tag, out var t) ? t : new List<PriceTranche>());
    }

    private class FakeRecipes : IRecipeSource
    {
        public Dictionary<string, List<RecipeOption>> Recipes { get; } = new();

        public bool TryGetRecipes(string tag, out IReadOnlyList<RecipeOption> recipes)
        {
            if (Recipes.TryGetValue(tag, out var r))
            {
                recipes = r;
                return true;
            }
            recipes = null!;
            return false;
        }
    }

    // OBSIDIAN: 30 each from npc up to 640 stock, then 50 each on the market.
    // ENCHANTED_OBSIDIAN: craftable from 160 OBSIDIAN, or bought for 6000 each.
    private static (FakeMarket market, FakeRecipes recipes) BuildObsidianWorld(long obsidianStock = 640)
    {
        var market = new FakeMarket();
        market.Tranches["OBSIDIAN"] = new()
        {
            new PriceTranche(30, obsidianStock, "npc"),
            new PriceTranche(50, 100_000_000, "insta"),
        };
        market.Tranches["ENCHANTED_OBSIDIAN"] = new()
        {
            new PriceTranche(6000, 100_000_000, "insta"),
        };
        var recipes = new FakeRecipes();
        recipes.Recipes["ENCHANTED_OBSIDIAN"] = new() { new RecipeOption(new List<(string tag, long count)> { ("OBSIDIAN", 160) }, 1) };
        return (market, recipes);
    }

    [Fact]
    public async Task SingleSubcraft_IsCraftedWhenCheaperAtSmallScale()
    {
        var (market, recipes) = BuildObsidianWorld();
        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 1, market, recipes);
        Assert.Equal("craft", result.Method);
        // 160 obsidian from npc, all within stock, plus the per-craft-step effort markup (1%) and flat coin.
        Assert.Equal(160 * 30 * 1.01 + 1, result.Cost);
        Assert.True(result.Enough);
    }

    [Fact]
    public async Task Subcraft_StopsBeingWorthItAtScale_WhenNpcStockRunsOut()
    {
        var (market, recipes) = BuildObsidianWorld();
        // Need 100 enchanted obsidian -> 16000 obsidian, but only 640 come cheap from the npc.
        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 100, market, recipes);
        Assert.Equal("buy", result.Method);
        Assert.Equal(100 * 6000, result.Cost); // buying is cheaper than crafting from stock-limited obsidian
    }

    [Fact]
    public async Task CraftCostReflectsBlendedNpcAndMarketAtScale()
    {
        var (market, recipes) = BuildObsidianWorld();
        // Force crafting to be evaluated by removing the direct buy option for enchanted obsidian.
        market.Tranches["ENCHANTED_OBSIDIAN"] = new();
        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 100, market, recipes);
        // 16000 obsidian: 640 @30 (npc) + 15360 @50 (market), plus the per-craft-step markup/flat coin.
        var rawCraftCost = 640 * 30 + 15360 * 50;
        var expected = rawCraftCost * 1.01 + 1;
        Assert.Equal("craft", result.Method);
        Assert.Equal(expected, result.Cost);
    }

    [Fact]
    public async Task BlendsBuyOrderVolumeThenInstaBuysTheRest()
    {
        var market = new FakeMarket();
        // 100 fill via buy order @10, the rest insta bought @20
        market.Tranches["ENDER_PEARL"] = new()
        {
            new PriceTranche(10, 100, "order"),
            new PriceTranche(20, 100_000_000, "insta"),
        };
        var recipes = new FakeRecipes();
        var result = await RealisticCraft.ObtainAsync("ENDER_PEARL", 250, market, recipes);
        Assert.Equal("buy", result.Method);
        Assert.Equal(100 * 10 + 150 * 20, result.Cost);
    }

    [Fact]
    public async Task PlanWalksEveryOfferAndUsesTheWeightedCostForTheRequestedQuantity()
    {
        var market = new FakeMarket();
        market.Tranches["ENDER_PEARL"] = new()
        {
            new PriceTranche(2.5, 71_000, "order"),
            new PriceTranche(9.8, 95_928, "insta"),
            new PriceTranche(9.9, 46_996, "insta"),
            new PriceTranche(10.9, 24, "insta"),
            new PriceTranche(11.0, 240, "insta"),
            new PriceTranche(11.1, 3_618, "insta"),
            new PriceTranche(11.2, 16_645, "insta"),
            new PriceTranche(11.3, 65_796, "insta"),
            new PriceTranche(20, 723_753, "insta")
        };

        var result = await RealisticCraft.ObtainAsync("ENDER_PEARL", 1_024_000, market, new FakeRecipes(),
            new RealisticCraft.Options { BuildPlan = true });

        Assert.Equal(17_030_895, result.Cost);
        Assert.Equal(71_000, result.Plan.Purchases.Where(p => p.Source == "order").Sum(p => p.Quantity));
        Assert.Equal(953_000, result.Plan.Purchases.Where(p => p.Source == "insta").Sum(p => p.Quantity));
        Assert.Equal(16_853_395, result.Plan.Purchases.Where(p => p.Source == "insta").Sum(p => p.Cost));
    }

    [Fact]
    public async Task PlanBuysOnlyCheapSupplyAndRepricesTheCraftedRemainder()
    {
        var market = new FakeMarket();
        market.Tranches["ENCHANTED_OBSIDIAN"] = new()
        {
            new PriceTranche(100, 3, "npc"),
            new PriceTranche(1000, 100, "insta")
        };
        market.Tranches["OBSIDIAN"] = new() { new PriceTranche(500, 100, "insta") };
        var recipes = new FakeRecipes();
        recipes.Recipes["ENCHANTED_OBSIDIAN"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("OBSIDIAN", 1) }, 1)
        };

        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 10, market, recipes,
            new RealisticCraft.Options { BuildPlan = true });

        Assert.Equal("craft", result.Method);
        Assert.Equal(3, result.Plan.Purchases.Sum(p => p.Quantity));
        Assert.Equal(7, result.Plan.CraftedQuantity);
        Assert.Equal(300 + (7 * 500 * 1.01 + 1), result.Cost);
        Assert.Equal(300 + 7 * 500, result.Plan.Cost);
        Assert.Equal(result.Plan.Cost, result.Plan.Purchases.Sum(p => p.Cost) + result.Plan.Ingredients.Sum(i => i.Cost));
    }

    [Fact]
    public async Task PlanRejectsSubcraftWhenItsFullQuantityCostExceedsDirectBuy()
    {
        var market = new FakeMarket();
        market.Tranches["ENCHANTED_OBSIDIAN"] = new() { new PriceTranche(3200, 6_144, "order") };
        market.Tranches["OBSIDIAN"] = new()
        {
            new PriceTranche(14, 640, "npc"),
            new PriceTranche(17.5, 71_000, "order"),
            new PriceTranche(26.3, 2_000_000, "insta")
        };
        var recipes = new FakeRecipes();
        recipes.Recipes["ENCHANTED_OBSIDIAN"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("OBSIDIAN", 160) }, 1)
        };

        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 6_144, market, recipes,
            new RealisticCraft.Options { BuildPlan = true });

        Assert.Equal("buy", result.Method);
        Assert.Equal(19_660_800, result.Cost);
        Assert.Equal(25_221_280, result.Plan.CraftCost);
        Assert.True(result.Plan.CraftCost > result.Plan.DirectBuyCost);
        Assert.Equal(0, result.Plan.CraftedQuantity);
    }

    [Fact]
    public async Task SiblingSubcraftsShareTheSameRemainingMarketOffers()
    {
        var market = new FakeMarket();
        market.Tranches["BASE"] = new()
        {
            new PriceTranche(1, 5, "insta"),
            new PriceTranche(100, 5, "insta")
        };
        var recipes = new FakeRecipes();
        recipes.Recipes["FIRST"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("BASE", 5) }, 1)
        };
        recipes.Recipes["SECOND"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("BASE", 5) }, 1)
        };
        recipes.Recipes["ROOT"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("FIRST", 1), ("SECOND", 1) }, 1)
        };

        var result = await RealisticCraft.ObtainCraftAsync("ROOT", 1, market, recipes,
            new RealisticCraft.Options { BuildPlan = true });

        var basePurchases = result.Plan.Ingredients
            .SelectMany(intermediate => intermediate.Ingredients)
            .SelectMany(baseItem => baseItem.Purchases)
            .ToList();
        Assert.Equal(5, basePurchases.Where(fill => fill.UnitPrice == 1).Sum(fill => fill.Quantity));
        Assert.Equal(5, basePurchases.Where(fill => fill.UnitPrice == 100).Sum(fill => fill.Quantity));
        Assert.Equal(result.Plan.Cost, result.Plan.Ingredients.Sum(ingredient => ingredient.Cost));
    }

    [Fact]
    public async Task BatchedTopLevelIngredientsShareTheSameRemainingMarketOffers()
    {
        var market = new FakeMarket();
        market.Tranches["BASE"] = new()
        {
            new PriceTranche(1, 5, "insta"),
            new PriceTranche(100, 5, "insta")
        };

        var results = await RealisticCraft.ObtainAllAsync(
            new List<(string tag, long quantity)> { ("BASE", 5), ("BASE", 5) }, market, new FakeRecipes());

        Assert.Equal(5, results[0].Cost);
        Assert.Equal(500, results[1].Cost);
    }

    [Fact]
    public async Task DeepChain_PrefersBuyingWhenCheaperThanCraftingAtScale()
    {
        // NULL_SPHERE <- 4 ENCHANTED_OBSIDIAN <- 160 OBSIDIAN each. Buying spheres @20k is cheapest.
        var (market, recipes) = BuildObsidianWorld();
        market.Tranches["NULL_SPHERE"] = new() { new PriceTranche(20_000, 100_000_000, "insta") };
        recipes.Recipes["NULL_SPHERE"] = new() { new RecipeOption(new List<(string tag, long count)> { ("ENCHANTED_OBSIDIAN", 4) }, 1) };
        var result = await RealisticCraft.ObtainAsync("NULL_SPHERE", 1000, market, recipes);
        Assert.Equal("buy", result.Method);
        Assert.Equal(1000 * 20_000, result.Cost);
    }

    [Fact]
    public async Task DeepChain_CraftsFromBoughtIntermediates_NotStockLimitedBase()
    {
        // With no direct market for null spheres they must be crafted, but the 640k obsidian needed is
        // far beyond npc stock, so the intermediates are bought rather than crafted from base obsidian.
        var (market, recipes) = BuildObsidianWorld();
        recipes.Recipes["NULL_SPHERE"] = new() { new RecipeOption(new List<(string tag, long count)> { ("ENCHANTED_OBSIDIAN", 4) }, 1) };
        var result = await RealisticCraft.ObtainAsync("NULL_SPHERE", 1000, market, recipes);
        Assert.Equal("craft", result.Method);
        // 4000 enchanted obsidian bought at 6000 each (no markup - they're bought, not crafted),
        // then the NULL_SPHERE craft step itself adds the markup/flat coin on top.
        var rawCraftCost = 4000 * 6000;
        Assert.Equal(rawCraftCost * 1.01 + 1, result.Cost);
    }

    [Fact]
    public async Task UnobtainableSupply_IsFlaggedAndPenalized()
    {
        var market = new FakeMarket();
        market.Tranches["RARE_ITEM"] = new() { new PriceTranche(100, 10, "insta") }; // only 10 available
        var recipes = new FakeRecipes();
        var result = await RealisticCraft.ObtainAsync("RARE_ITEM", 1000, market, recipes);
        Assert.False(result.Enough);
        Assert.True(result.Cost > 1000 * 100); // penalized above the naive price
    }

    [Fact]
    public async Task DepthTruncatedResult_IsNotMemoizedAcrossShallowerEvaluation()
    {
        // T needs M (which needs X) and, separately, X directly.
        // With MaxDepth = 2, the X requested through M lands at depth 2 (>= MaxDepth), so crafting is
        // skipped there and X is bought only - a context-dependent ("not exact") result. T's own
        // direct need for X lands at depth 1 (< MaxDepth), where crafting X (from cheap npc BASE) is
        // actually cheaper than buying it. Because ingredients are evaluated in recipe order (M before
        // the direct X), the depth-2 buy-only result for X is computed first. Without the fix in
        // RealisticCraft.ObtainAsync, that buy-only result would be memoized under (X, 1) and wrongly
        // reused for the depth-1 evaluation, hiding the cheaper craft.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(30, 100_000_000, "npc") };
        market.Tranches["X"] = new() { new PriceTranche(6000, 100_000_000, "insta") };
        // M and T have no buy tranches at all, so buying them is never "Enough" and crafting always wins.
        var recipes = new FakeRecipes();
        recipes.Recipes["X"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 160) }, 1) };
        recipes.Recipes["M"] = new() { new RecipeOption(new List<(string tag, long count)> { ("X", 1) }, 1) };
        recipes.Recipes["T"] = new() { new RecipeOption(new List<(string tag, long count)> { ("M", 1), ("X", 1) }, 1) };
        var options = new RealisticCraft.Options { MaxDepth = 2 };

        var result = await RealisticCraft.ObtainAsync("T", 1, market, recipes, options);

        // X crafted from BASE (correct, unpoisoned depth-1 evaluation).
        var xCraftEffective = 160 * 30 * options.CraftStepMarkup + options.CraftStepFlatCoins;
        // M forced to craft from a bought X (depth-2 evaluation truncated by MaxDepth, buy-only, cost = buy price).
        var mCraftEffective = 6000 * options.CraftStepMarkup + options.CraftStepFlatCoins;
        var tCraftCost = mCraftEffective + xCraftEffective;
        var tExpected = tCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;

        Assert.Equal("craft", result.Method);
        Assert.Equal(tExpected, result.Cost, 6);
        // The poisoned (buggy) result would instead reuse the depth-2 buy-only X (6000) for the direct
        // X ingredient too, producing a strictly larger total cost. Guard against that regression.
        var poisonedXEffective = 6000d; // buy cost reused verbatim from the depth-2 memo entry
        var poisonedTCraftCost = mCraftEffective + poisonedXEffective;
        var poisonedTExpected = poisonedTCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.True(result.Cost < poisonedTExpected);
    }

    [Fact]
    public async Task DeepBuyWinsResult_ConsumingTruncatedSub_IsNotMemoizedAcrossShallowerEvaluation()
    {
        // T needs M (which needs Y, which needs X) and, separately, Y directly.
        // With MaxDepth = 3: via M, Y lands at depth 2 (craft branch runs there - 2 < MaxDepth), but Y's
        // own ingredient X lands at depth 3 (>= MaxDepth), so X's craft is skipped and X is bought
        // (a non-exact result). Fed that bought-X price, crafting Y is NOT worth it there (buy wins for
        // Y) - this is the residual gap: the buy-wins outcome still consumed a non-exact sub (X), so it
        // must not be memoized either, even though the "exact = subsExact" line historically only ran
        // inside the craft-wins branch.
        // T's own direct need for Y lands at depth 1, where Y's ingredient X lands at depth 2 (< MaxDepth)
        // and is actually crafted (cheap npc BASE), making crafting Y worth it there instead.
        // Ingredients are evaluated in recipe order (M before the direct Y), so the deep buy-wins Y result
        // is computed first. Without the fix, that buy-only (but non-exact) result would be memoized under
        // (Y, 1) and wrongly reused for the depth-1 evaluation, hiding the cheaper craft.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(30, 100_000_000, "npc") };
        market.Tranches["X"] = new() { new PriceTranche(6000, 100_000_000, "insta") };
        market.Tranches["Y"] = new() { new PriceTranche(6000, 100_000_000, "insta") };
        // M has no buy option at all, so buying it is never "Enough" and crafting M always wins.
        var recipes = new FakeRecipes();
        recipes.Recipes["X"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 160) }, 1) };
        recipes.Recipes["Y"] = new() { new RecipeOption(new List<(string tag, long count)> { ("X", 1) }, 1) };
        recipes.Recipes["M"] = new() { new RecipeOption(new List<(string tag, long count)> { ("Y", 1) }, 1) };
        recipes.Recipes["T"] = new() { new RecipeOption(new List<(string tag, long count)> { ("M", 1), ("Y", 1) }, 1) };
        var options = new RealisticCraft.Options { MaxDepth = 3 };

        var result = await RealisticCraft.ObtainAsync("T", 1, market, recipes, options);

        // X crafted from BASE (depth 2 via T's direct Y - below MaxDepth, so not skipped).
        var xCraftEffective = 160 * 30 * options.CraftStepMarkup + options.CraftStepFlatCoins;
        // Y (direct, depth 1) crafted from that cheap, correctly-crafted X.
        var yDirectCraftEffective = xCraftEffective * options.CraftStepMarkup + options.CraftStepFlatCoins;
        // Y (via M, depth 2) has its own X ingredient truncated by MaxDepth at depth 3, so X is bought
        // there instead (6000) - correctly making buy win for that deep Y evaluation.
        var yViaMCost = 6000d;
        // M crafts from that (correctly non-memoized, freshly recomputed) buy-only Y.
        var mCraftEffective = yViaMCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        var tCraftCost = mCraftEffective + yDirectCraftEffective;
        var tExpected = tCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;

        Assert.Equal("craft", result.Method);
        Assert.Equal(tExpected, result.Cost, 6);
        // The poisoned (buggy) result would instead reuse the deep, non-exact buy-only Y (6000) for the
        // direct Y ingredient too (skipping the cheaper craft entirely), producing a strictly larger total.
        var poisonedYDirect = yViaMCost; // buy cost wrongly reused verbatim from the depth-2 memo entry
        var poisonedTCraftCost = mCraftEffective + poisonedYDirect;
        var poisonedTExpected = poisonedTCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.True(result.Cost < poisonedTExpected);
    }

    [Fact]
    public async Task CraftStepMarkup_CompoundsUpAMultiLevelAllCraftChain()
    {
        // OUTER <- 2x INNER <- 10x BASE, both directly craftable, and neither OUTER nor INNER has a
        // buy option (so crafting is the only route at each level). The per-step effort markup should
        // apply at BOTH craft steps and compound, so it must show up twice in the reported top cost.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(30, 100_000_000, "npc") };
        var recipes = new FakeRecipes();
        recipes.Recipes["INNER"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 10) }, 1) };
        recipes.Recipes["OUTER"] = new() { new RecipeOption(new List<(string tag, long count)> { ("INNER", 2) }, 1) };
        var options = new RealisticCraft.Options();

        var result = await RealisticCraft.ObtainAsync("OUTER", 5, market, recipes, options);

        // 5 OUTER -> 10 INNER -> 100 BASE @30 = 3000 raw.
        var rawCraftCost = 10 * 30 * 2 * 5;
        var effectiveInner = rawCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        var effectiveOuter = effectiveInner * options.CraftStepMarkup + options.CraftStepFlatCoins;

        Assert.Equal("craft", result.Method);
        Assert.Equal(effectiveOuter, result.Cost, 6);
        Assert.True(result.Cost > rawCraftCost); // the markup must actually have propagated up
    }

    // --- Fix #1: pseudo-currency ingredients (SKYBLOCK_BIT / SKYBLOCK_COPPER / SKYBLOCK_MOTE) ---

    [Fact]
    public async Task Bit_IsPricedAtLiveRate_AndFlaggedNonNormal()
    {
        var market = new FakeMarket();
        var recipes = new FakeRecipes();
        var options = new RealisticCraft.Options { CoinsPerBit = 4321 };

        var result = await RealisticCraft.ObtainAsync("SKYBLOCK_BIT", 10, market, recipes, options);

        Assert.Equal("bits", result.Method); // distinct marker, not "buy"/"craft"/"npc"
        Assert.Equal(10 * 4321d, result.Cost);
        Assert.True(result.Enough); // priced, not abandoned
    }

    [Fact]
    public async Task Copper_IsPricedAtLiveRate_AndFlaggedNonNormal()
    {
        var market = new FakeMarket();
        var recipes = new FakeRecipes();
        var options = new RealisticCraft.Options { CoinsPerCopper = 1234 };

        var result = await RealisticCraft.ObtainAsync("SKYBLOCK_COPPER", 7, market, recipes, options);

        Assert.Equal("copper", result.Method);
        Assert.Equal(7 * 1234d, result.Cost);
        Assert.True(result.Enough);
    }

    [Fact]
    public async Task Mote_StaysUnobtainable_ButIsFlaggedNonNormal()
    {
        var market = new FakeMarket();
        var recipes = new FakeRecipes();
        var options = new RealisticCraft.Options();

        var result = await RealisticCraft.ObtainAsync("SKYBLOCK_MOTE", 5, market, recipes, options);

        Assert.Equal("mote", result.Method); // distinct marker even though unobtainable
        Assert.False(result.Enough); // no representative coin value invented for motes
        Assert.Equal(5 * options.UnmetFallbackUnitPrice, result.Cost);
    }

    // --- Fix #3 (corrected): no cost inflation for indirect/time-gated steps at all. Forge, malik and
    // npc_shop steps all use the same small uniform per-step markup as a direct craft - the real coin
    // cost is what matters (e.g. for SkySniper's CraftCostService value cap); keeping these out of
    // "craft flip" results is the Type marker's job (CalculatorService.ResolveCraftType /
    // CraftsController.GetProfitable), not cost inflation here.

    [Fact]
    public async Task ForgeStep_UsesTheSameSmallMarkup_NotAHundredXPenalty()
    {
        // FORGED_MAT is not directly craftable (a genuinely time-gated forge recipe), but forge only
        // gates time, not coin cost, so it must be priced with the same small step markup as any
        // direct craft - never inflated.
        var market = new FakeMarket();
        market.Tranches["RAW"] = new() { new PriceTranche(25, 100_000_000, "npc") };
        var recipes = new FakeRecipes();
        recipes.Recipes["FORGED_MAT"] = new() { new RecipeOption(new List<(string tag, long count)> { ("RAW", 1) }, 1) }; // indirect (forge)
        var options = new RealisticCraft.Options();

        var result = await RealisticCraft.ObtainAsync("FORGED_MAT", 25, market, recipes, options);

        Assert.Equal("craft", result.Method);
        var rawCraftCost = 25 * 25;
        var expected = rawCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.Equal(expected, result.Cost, 6);
        // No 100x (or any other) blowup anywhere: cost must stay within a small multiple of the raw
        // ingredient cost.
        Assert.True(result.Cost < rawCraftCost * 1.1);
    }

    [Fact]
    public async Task NpcShopStep_AlsoUsesTheSameSmallMarkup()
    {
        // Same shape, but the indirect step is merely an npc-shop purchase - still no inflation, and
        // it should cost exactly the same (uniform markup) as the forge-sourced case above.
        var market = new FakeMarket();
        market.Tranches["RAW"] = new() { new PriceTranche(25, 100_000_000, "npc") };
        var recipes = new FakeRecipes();
        recipes.Recipes["BASE_MAT"] = new() { new RecipeOption(new List<(string tag, long count)> { ("RAW", 1) }, 1) }; // indirect (npc_shop)
        var options = new RealisticCraft.Options();

        var result = await RealisticCraft.ObtainAsync("BASE_MAT", 25, market, recipes, options);

        Assert.Equal("craft", result.Method);
        var rawCraftCost = 25 * 25;
        var expected = rawCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.Equal(expected, result.Cost, 6);
    }

    // --- Fix #4: multiple recipes per item - the engine must evaluate ALL of them and pick the
    // cheapest, mirroring the real ENCHANTED_GOLD bug (GOLD_INGOT vs the pointless GOLD_BLOCK detour).

    [Fact]
    public async Task PrefersCheaperRecipe_WhenItemHasMultipleRecipes()
    {
        // BASE: deep, cheap npc-scale insta supply.
        // BLOCK: no buy tranche of its own, craftable from 9 BASE (yield 1).
        // ENCH: no buy tranche (crafting forced), two recipes: direct from BASE, or via 160 BLOCK
        // (= 1440 BASE) - a pointless detour that must lose.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(1, 100_000_000, "insta") };
        var recipes = new FakeRecipes();
        recipes.Recipes["BLOCK"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 9) }, 1) };
        recipes.Recipes["ENCH"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("BASE", 160) }, 1),  // direct: cheap
            new RecipeOption(new List<(string tag, long count)> { ("BLOCK", 160) }, 1), // via block: 1440 BASE - pointless detour
        };

        var result = await RealisticCraft.ObtainAsync("ENCH", 1, market, recipes);

        Assert.Equal("craft", result.Method);
        Assert.True(result.Enough);
        var options = new RealisticCraft.Options();
        var directExpected = 160 * 1 * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.Equal(directExpected, result.Cost, 6);
        // Sanity: the block route would cost roughly 1440 raw (9x more) - make sure that is NOT what won.
        var blockRouteApprox = 1440 * 1;
        Assert.True(result.Cost < blockRouteApprox);
    }

    [Fact]
    public async Task PrefersCheaperRecipe_RegardlessOfCandidateOrder()
    {
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(1, 100_000_000, "insta") };
        var recipes = new FakeRecipes();
        recipes.Recipes["BLOCK"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 9) }, 1) };
        // Same two recipes, but with the block-route (expensive) one listed FIRST.
        recipes.Recipes["ENCH"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("BLOCK", 160) }, 1), // via block: 1440 BASE - pointless detour
            new RecipeOption(new List<(string tag, long count)> { ("BASE", 160) }, 1),  // direct: cheap
        };

        var result = await RealisticCraft.ObtainAsync("ENCH", 1, market, recipes);

        Assert.Equal("craft", result.Method);
        Assert.True(result.Enough);
        var options = new RealisticCraft.Options();
        var directExpected = 160 * 1 * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.Equal(directExpected, result.Cost, 6);
    }

    // --- Shared per-pass memo (CalculatorService threads one memo across every item in a pass) ---

    /// <summary>Wraps a market and counts how many times GetBuyTranchesAsync was called per tag.</summary>
    private class CountingMarket : IMarketSource
    {
        private readonly IMarketSource inner;
        private readonly ConcurrentDictionary<string, int> counts = new();
        public CountingMarket(IMarketSource inner) => this.inner = inner;
        public int CallsFor(string tag) => counts.TryGetValue(tag, out var c) ? c : 0;
        public Task<IReadOnlyList<PriceTranche>> GetBuyTranchesAsync(string tag)
        {
            counts.AddOrUpdate(tag, 1, (_, existing) => existing + 1);
            return inner.GetBuyTranchesAsync(tag);
        }
    }

    // BASE: cheap, effectively unlimited npc supply.
    // SHARED: craftable from 10 BASE, no buy option (crafting forced) - shared sub-craft between two parents.
    // PARENT_A / PARENT_B: each craftable from 5 SHARED (same quantity!), no buy option, so both parents
    // hit the exact same (SHARED, 5) memo key.
    private static (FakeMarket market, FakeRecipes recipes) BuildSharedSubcraftWorld()
    {
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(1, 100_000_000, "npc") };
        var recipes = new FakeRecipes();
        recipes.Recipes["SHARED"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 10) }, 1) };
        recipes.Recipes["PARENT_A"] = new() { new RecipeOption(new List<(string tag, long count)> { ("SHARED", 5) }, 1) };
        recipes.Recipes["PARENT_B"] = new() { new RecipeOption(new List<(string tag, long count)> { ("SHARED", 5) }, 1) };
        return (market, recipes);
    }

    [Fact]
    public async Task SharedMemo_ProducesIdenticalResults_ToIndependentFreshMemos()
    {
        var (market, recipes) = BuildSharedSubcraftWorld();
        var options = new RealisticCraft.Options();

        // Independent evaluation: each parent uses the public overload, which allocates its own fresh memo.
        var independentA = await RealisticCraft.ObtainAsync("PARENT_A", 1, market, recipes, options);
        var independentB = await RealisticCraft.ObtainAsync("PARENT_B", 1, market, recipes, options);

        // Shared evaluation: both parents obtained against one shared memo (as CalculatorService now does
        // for a whole pricing pass).
        var sharedMemo = new ConcurrentDictionary<(string, long), Obtainment>();
        var sharedA = await RealisticCraft.ObtainAsync("PARENT_A", 1, market, recipes, options, sharedMemo);
        var sharedB = await RealisticCraft.ObtainAsync("PARENT_B", 1, market, recipes, options, sharedMemo);

        Assert.Equal(independentA.Cost, sharedA.Cost);
        Assert.Equal(independentA.Method, sharedA.Method);
        Assert.Equal(independentA.Enough, sharedA.Enough);
        Assert.Equal(independentB.Cost, sharedB.Cost);
        Assert.Equal(independentB.Method, sharedB.Method);
        Assert.Equal(independentB.Enough, sharedB.Enough);
    }

    [Fact]
    public async Task SharedMemo_AvoidsRecomputingSharedSubcraft_AcrossParents()
    {
        var (rawMarket, recipes) = BuildSharedSubcraftWorld();
        var options = new RealisticCraft.Options();

        // Fresh-memo-per-parent baseline: the shared sub-craft (and its BASE ingredient) is recomputed
        // from scratch for the second parent.
        var freshCountingMarket = new CountingMarket(rawMarket);
        await RealisticCraft.ObtainAsync("PARENT_A", 1, freshCountingMarket, recipes, options);
        await RealisticCraft.ObtainAsync("PARENT_B", 1, freshCountingMarket, recipes, options);
        var freshSharedCalls = freshCountingMarket.CallsFor("SHARED");

        // Shared-memo pass: the second parent's identical (SHARED, 5) need is served straight from the memo.
        var sharedCountingMarket = new CountingMarket(rawMarket);
        var sharedMemo = new ConcurrentDictionary<(string, long), Obtainment>();
        await RealisticCraft.ObtainAsync("PARENT_A", 1, sharedCountingMarket, recipes, options, sharedMemo);
        await RealisticCraft.ObtainAsync("PARENT_B", 1, sharedCountingMarket, recipes, options, sharedMemo);
        var sharedSharedCalls = sharedCountingMarket.CallsFor("SHARED");

        Assert.True(sharedSharedCalls < freshSharedCalls, $"expected shared-memo calls ({sharedSharedCalls}) to be strictly fewer than fresh-memo calls ({freshSharedCalls})");
        Assert.Equal(1, sharedSharedCalls); // computed exactly once, then served from the memo
        Assert.Equal(2, freshSharedCalls); // recomputed once per parent without a shared memo
    }

    [Fact]
    public async Task SharedMemo_KeysByQuantity_DoesNotCollideAcrossDifferentQuantities()
    {
        // ITEM: 5 units cheap via npc @10, everything beyond that insta bought @100. Quantity 3 stays
        // entirely within the cheap npc tranche; quantity 10 spills into the expensive insta tranche, so
        // the two quantities must yield genuinely different per-unit costs.
        var market = new FakeMarket();
        market.Tranches["ITEM"] = new()
        {
            new PriceTranche(10, 5, "npc"),
            new PriceTranche(100, 100_000_000, "insta"),
        };
        var recipes = new FakeRecipes();
        var sharedMemo = new ConcurrentDictionary<(string, long), Obtainment>();

        var small = await RealisticCraft.ObtainAsync("ITEM", 3, market, recipes, new RealisticCraft.Options(), sharedMemo);
        var large = await RealisticCraft.ObtainAsync("ITEM", 10, market, recipes, new RealisticCraft.Options(), sharedMemo);

        Assert.Equal(3 * 10d, small.Cost);
        Assert.Equal(5 * 10d + 5 * 100d, large.Cost);
        Assert.NotEqual(small.Cost / 3, large.Cost / 10); // no cross-quantity collision in the shared memo
    }

    [Fact]
    public async Task SharedMemo_IsSafeUnderConcurrentAccess()
    {
        var (market, recipes) = BuildSharedSubcraftWorld();
        var options = new RealisticCraft.Options();
        var sharedMemo = new ConcurrentDictionary<(string, long), Obtainment>();
        var expected = await RealisticCraft.ObtainAsync("PARENT_A", 1, market, recipes, options);

        var tasks = Enumerable.Range(0, 50)
            .Select(i => RealisticCraft.ObtainAsync(i % 2 == 0 ? "PARENT_A" : "PARENT_B", 1, market, recipes, options, sharedMemo))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r =>
        {
            Assert.Equal("craft", r.Method);
            Assert.Equal(expected.Cost, r.Cost);
            Assert.True(r.Enough);
        });
    }

    // --- Bulk-order craft-step penalty (Options.MaxSingleOrderQuantity / BulkCraftStepMarkup) ---
    // A single bazaar buy order can not hold more than MaxSingleOrderQuantity units of one item, so a
    // craft step that needs more of an ingredient than that takes several sequential orders (moving the
    // price) and should be priced with the higher BulkCraftStepMarkup instead of the normal CraftStepMarkup.

    [Fact]
    public async Task BulkOrdering_NotTriggered_WhenIngredientStaysAtOrBelowCap()
    {
        // ITEM has no buy tranche (forces crafting) and is craftable 1:1 from BASE. Requesting exactly
        // MaxSingleOrderQuantity units means the ingredient need equals the cap - not over it - so the
        // normal per-step markup applies.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(1, 1_000_000_000, "npc") };
        var recipes = new FakeRecipes();
        recipes.Recipes["ITEM"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 1) }, 1) };
        var options = new RealisticCraft.Options();

        var result = await RealisticCraft.ObtainAsync("ITEM", options.MaxSingleOrderQuantity, market, recipes, options);

        Assert.Equal("craft", result.Method);
        var rawCraftCost = options.MaxSingleOrderQuantity * 1d;
        var expected = rawCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins;
        Assert.Equal(expected, result.Cost, 6);
    }

    [Fact]
    public async Task BulkOrdering_Triggered_WhenIngredientExceedsCap()
    {
        // Same shape as above, but one unit past the cap: the ingredient need (MaxSingleOrderQuantity + 1)
        // now exceeds a single bazaar order, so the higher BulkCraftStepMarkup must apply instead.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(1, 1_000_000_000, "npc") };
        var recipes = new FakeRecipes();
        recipes.Recipes["ITEM"] = new() { new RecipeOption(new List<(string tag, long count)> { ("BASE", 1) }, 1) };
        var options = new RealisticCraft.Options();
        var quantity = options.MaxSingleOrderQuantity + 1;

        var result = await RealisticCraft.ObtainAsync("ITEM", quantity, market, recipes, options);

        Assert.Equal("craft", result.Method);
        var rawCraftCost = quantity * 1d;
        var expected = rawCraftCost * options.BulkCraftStepMarkup + options.CraftStepFlatCoins;
        Assert.Equal(expected, result.Cost, 6);
    }

    // --- BuyCost: expose the "buy it outright" alternative alongside a chosen craft/buy result ---

    [Fact]
    public async Task CraftedResult_CarriesBuyCostAsTheViableBuyAlternative()
    {
        var (market, recipes) = BuildObsidianWorld();
        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 1, market, recipes);

        Assert.Equal("craft", result.Method);
        // Buying directly is viable (100_000_000 capacity insta tranche @6000) even though crafting won.
        Assert.Equal(6000d, result.BuyCost);
        Assert.True(result.BuyCost > result.Cost); // the buy alternative is pricier than the chosen craft
    }

    [Fact]
    public async Task BoughtResult_HasBuyCostEqualToCost()
    {
        var (market, recipes) = BuildObsidianWorld();
        // Force buying to win (crafting is not worth it at this scale, see Subcraft_StopsBeingWorthItAtScale_WhenNpcStockRunsOut).
        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 100, market, recipes);

        Assert.Equal("buy", result.Method);
        Assert.Equal(result.Cost, result.BuyCost);
    }

    [Fact]
    public async Task BuyCostIsZero_WhenNoViableBuyAlternativeExists()
    {
        // No buy tranches at all for ENCHANTED_OBSIDIAN: crafting is the only route, and there is no
        // buy alternative to report.
        var (market, recipes) = BuildObsidianWorld();
        market.Tranches["ENCHANTED_OBSIDIAN"] = new();

        var result = await RealisticCraft.ObtainAsync("ENCHANTED_OBSIDIAN", 1, market, recipes);

        Assert.Equal("craft", result.Method);
        Assert.Equal(0d, result.BuyCost);
    }

    [Fact]
    public async Task PriceIngredientsAsync_CraftedIngredient_ReportsBuyOrderCostAboveCraftCost()
    {
        // ENCHANTED_OBSIDIAN is cheaper to craft than to buy (see SingleSubcraft_IsCraftedWhenCheaperAtSmallScale),
        // and PLAIN_OBSIDIAN below is only ever bought.
        var (market, recipes) = BuildObsidianWorld();
        var options = new RealisticCraft.Options();
        var memo = new ConcurrentDictionary<(string, long), Obtainment>();
        var ingredients = new List<Ingredient>
        {
            new() { ItemId = "ENCHANTED_OBSIDIAN", Count = 1 },
            new() { ItemId = "OBSIDIAN", Count = 1_000_000 }, // far beyond npc stock -> bought on the market
        };

        await CalculatorService.PriceIngredientsAsync(ingredients, market, recipes, options, memo);

        var crafted = ingredients.Single(i => i.ItemId == "ENCHANTED_OBSIDIAN");
        Assert.Equal(crafted.Cost, crafted.CraftCost); // it was crafted: Cost == CraftCost
        Assert.True(crafted.BuyOrderCost > crafted.CraftCost); // buying outright would have cost more
        Assert.Equal(6000d, crafted.BuyOrderCost); // the genuine buy-it-outright price

        var bought = ingredients.Single(i => i.ItemId == "OBSIDIAN");
        Assert.Equal(0, bought.CraftCost); // bought, not crafted
        Assert.Equal(bought.Cost, bought.BuyOrderCost); // no cheaper buy alternative than what was actually paid
    }

    [Fact]
    public async Task PriceIngredientsAsync_PopulatesNpcOrderAndInstaAcquisitionFields()
    {
        // OBSIDIAN has npc + insta tranches (see BuildObsidianWorld); add an "order" tranche too so the
        // npc, buy-order and insta buckets are each populated separately.
        var (market, recipes) = BuildObsidianWorld();
        market.Tranches["OBSIDIAN"].Add(new PriceTranche(40, 100, "order"));
        var options = new RealisticCraft.Options();
        var memo = new ConcurrentDictionary<(string, long), Obtainment>();
        var ingredients = new List<Ingredient> { new() { ItemId = "OBSIDIAN", Count = 1 } };

        await CalculatorService.PriceIngredientsAsync(ingredients, market, recipes, options, memo);

        var obsidian = ingredients.Single();
        Assert.Equal(640, obsidian.NpcCapacity);
        Assert.Equal(30d, obsidian.NpcUnitPrice, 6);
        Assert.Equal(100, obsidian.BuyOrderCapacity);
        Assert.Equal(40d, obsidian.BuyOrderUnitPrice, 6);
        Assert.Equal(100_000_000, obsidian.InstaBuyCapacity);
        Assert.Equal(50d, obsidian.InstaBuyUnitPrice);
    }

    [Fact]
    public async Task BulkOrdering_IsPerCandidate_NotGlobalAcrossRecipes()
    {
        // ITEM has two recipes: a cheap direct one that stays well under the single-order cap, and a
        // pricier one that needs a huge amount of a different ingredient (over the cap). The bulk markup
        // decision must be scoped to each candidate individually - the winning (cheap, under-cap)
        // candidate must keep the normal markup even though the losing candidate would have needed bulk
        // ordering. A buggy "global" implementation (bulk if ANY candidate needs it) would instead apply
        // BulkCraftStepMarkup to the winner too, producing a different (higher) cost than asserted here.
        var market = new FakeMarket();
        market.Tranches["BASE"] = new() { new PriceTranche(1, 1_000_000_000, "npc") };
        market.Tranches["OTHER"] = new() { new PriceTranche(10, 1_000_000_000, "insta") };
        var recipes = new FakeRecipes();
        recipes.Recipes["ITEM"] = new()
        {
            new RecipeOption(new List<(string tag, long count)> { ("BASE", 1) }, 1),     // cheap, stays under cap
            new RecipeOption(new List<(string tag, long count)> { ("OTHER", 1000) }, 1), // pricier, goes over cap
        };
        var options = new RealisticCraft.Options();
        var quantity = 100; // BASE need: 1*100=100 (well under cap); OTHER need: 1000*100=100000 (over cap)

        var result = await RealisticCraft.ObtainAsync("ITEM", quantity, market, recipes, options);

        Assert.Equal("craft", result.Method);
        var rawCraftCost = quantity * 1d; // via BASE - the cheap, under-cap candidate wins
        var expected = rawCraftCost * options.CraftStepMarkup + options.CraftStepFlatCoins; // normal, not bulk, markup
        Assert.Equal(expected, result.Cost, 6);
    }
}
