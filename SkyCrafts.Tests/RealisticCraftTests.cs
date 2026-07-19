using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
}
