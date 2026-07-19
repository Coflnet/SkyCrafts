using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Crafts.Services;

/// <summary>
/// A single price level a buyer can source units from (an npc offer, filling buy orders,
/// or one order of the insta-buy order book). Tranches are combined cheapest-first to model
/// a smart buyer that spreads a large order across every realistic supply channel.
/// </summary>
public readonly struct PriceTranche
{
    /// <summary>Price per single unit sourced from this tranche.</summary>
    public double UnitPrice { get; }
    /// <summary>How many units can realistically be sourced here (npc stock, ~1h of insta-sell volume, order book depth).</summary>
    public long Capacity { get; }
    /// <summary>Where these units come from: "npc", "order" (buy order fills) or "insta" (walking the sell offer book).</summary>
    public string Source { get; }

    public PriceTranche(double unitPrice, long capacity, string source)
    {
        UnitPrice = unitPrice;
        Capacity = capacity;
        Source = source;
    }
}

/// <summary>
/// Result of figuring out how to obtain a quantity of an item.
/// </summary>
/// <param name="Cost">Total realistic cost to obtain <c>Quantity</c> units.</param>
/// <param name="Enough">True when the quantity could actually be sourced (supply was not exhausted).</param>
/// <param name="Method">How it is obtained as a whole: "craft", "npc" or "buy".</param>
public record Obtainment(double Cost, bool Enough, string Method);

/// <summary>
/// Fills an order from the cheapest available price tranches first, respecting each tranche's
/// capacity. This models a realistic buyer: cheap npc stock and buy-order fills run out, so the
/// remainder has to be insta-bought deeper into the order book (or is unobtainable).
/// </summary>
public static class SmartBuyer
{
    /// <summary>
    /// Sources <paramref name="quantity"/> units from <paramref name="tranches"/> cheapest first.
    /// </summary>
    /// <returns>
    /// filled: cost of the units that could be sourced;
    /// unmet: units that could not be sourced from any tranche;
    /// dominantSource: the source that contributed the most units.
    /// </returns>
    public static (double filled, long unmet, string dominantSource) Cost(IEnumerable<PriceTranche> tranches, long quantity)
    {
        if (quantity <= 0)
            return (0, 0, "buy");
        double cost = 0;
        long remaining = quantity;
        var perSource = new Dictionary<string, long>();
        foreach (var tranche in tranches.Where(t => t.Capacity > 0 && t.UnitPrice >= 0).OrderBy(t => t.UnitPrice))
        {
            if (remaining <= 0)
                break;
            var take = Math.Min(remaining, tranche.Capacity);
            cost += take * tranche.UnitPrice;
            remaining -= take;
            perSource.TryGetValue(tranche.Source, out var existing);
            perSource[tranche.Source] = existing + take;
        }
        var dominant = perSource.Count == 0 ? "buy" : perSource.OrderByDescending(p => p.Value).First().Key;
        return (cost, Math.Max(0, remaining), dominant);
    }
}

/// <summary>Provides the price tranches an item can be bought from (npc + market).</summary>
public interface IMarketSource
{
    /// <summary>
    /// Returns the (quantity independent) tranches <paramref name="tag"/> can be bought from.
    /// The underlying market data (bazaar batch + order books) this is built from is fetched once
    /// per pricing pass and cached by the implementation; the per-tag tranches themselves are cheap
    /// to recompute and are NOT cached, so this may be called repeatedly for the same tag.
    /// </summary>
    Task<IReadOnlyList<PriceTranche>> GetBuyTranchesAsync(string tag);
}

/// <summary>Provides crafting recipes so the realistic calculator can expand sub-crafts.</summary>
public interface IRecipeSource
{
    /// <summary>
    /// Gets the recipe for <paramref name="tag"/> if it is craftable.
    /// </summary>
    /// <param name="ingredients">Aggregated ingredients (tag + count) needed for one batch.</param>
    /// <param name="yield">How many output items one batch of the recipe produces (>= 1).</param>
    /// <param name="directlyCraftable">False for forge/npc-shop style recipes that cannot be crafted instantly.</param>
    bool TryGetRecipe(string tag, out IReadOnlyList<(string tag, long count)> ingredients, out long yield, out bool directlyCraftable);
}

/// <summary>
/// Computes the realistic cost to obtain a quantity of an item, choosing between buying it and
/// crafting it (recursively) using quantity aware, supply limited market pricing. Because it works
/// with the true quantities needed (e.g. ~1M obsidian for one end game weapon), sub-crafts that only
/// look cheap while ignoring npc stock and bazaar volume correctly stop being chosen at scale.
/// </summary>
public static class RealisticCraft
{
    public class Options
    {
        /// <summary>Maximum recursion depth into sub-crafts.</summary>
        public int MaxDepth { get; set; } = 12;
        /// <summary>Crafting must be at least this factor cheaper than buying to be chosen (covers effort/risk).</summary>
        public double CraftPreferenceMargin { get; set; } = 1.02;
        /// <summary>
        /// Effort markup applied per craft step (>= 1% by default), direct OR indirect (forge, malik,
        /// npc_shop, carpentry, trade, ...), so the extra work of crafting propagates up a chain. There is
        /// no separate multiplier for indirect/time-gated steps: supply/liquidity limits are already
        /// modeled by tranche capacities (npc stock caps, order-book depth), forge is time-gated but costs
        /// the same coins, and malik (Kuudra upgrade) recipes are unlimited and cost essence - none of
        /// that is a reason to inflate the coin cost. Keeping forge/malik/etc. out of "craft flip" results
        /// is handled entirely by the Type marker (see CalculatorService.ResolveCraftType /
        /// CraftsController.GetProfitable), not by cost inflation here. This also matters because
        /// downstream consumers (e.g. SkySniper's CraftCostService) use CraftCost as a real value cap
        /// (craftCost * stackSize * margin) - an inflated craft cost would corrupt that ceiling.
        /// </summary>
        public double CraftStepMarkup { get; set; } = 1.01;
        /// <summary>Flat coin cost added per craft step on top of the markup.</summary>
        public double CraftStepFlatCoins { get; set; } = 1;
        /// <summary>Unit price multiplier applied to units that could not be sourced, so unobtainable-at-scale crafts stay expensive but finite.</summary>
        public double UnmetPenaltyFactor { get; set; } = 5;
        /// <summary>Fallback unit price for unmet demand when no tranche price is known.</summary>
        public double UnmetFallbackUnitPrice { get; set; } = 20_000_000;

        /// <summary>
        /// Representative coin value of one SKYBLOCK_BIT, used to price SKYBLOCK_BIT ingredients instead of
        /// treating them as an unobtainable count*20M fallback. Normally overwritten by CalculatorService with
        /// a live rate (max over bit-shop mappings of bazaarBuyPrice/bitValue) each pricing pass; this default
        /// is only a conservative fallback for when that live lookup fails.
        /// </summary>
        public double CoinsPerBit { get; set; } = 500;
        /// <summary>
        /// Representative coin value of one SKYBLOCK_COPPER, used to price SKYBLOCK_COPPER ingredients instead
        /// of the unobtainable count*20M fallback. Normally overwritten by CalculatorService with a live rate
        /// (cheapest sky-bazaar-flipper /copper acquisition cost) each pricing pass; this default of 2000 is a
        /// conservative fallback matching the flat AnalyzeCost/CopperYield floor (2000) shared by essentially
        /// every SkyBazaarFlipper.Constants.CopperConstants entry, ignoring the (always >= 0) item buy price.
        /// </summary>
        public double CoinsPerCopper { get; set; } = 2000;
    }

    /// <summary>
    /// Figures out the cheapest realistic way to obtain <paramref name="quantity"/> of <paramref name="tag"/>.
    /// </summary>
    public static async Task<Obtainment> ObtainAsync(string tag, long quantity, IMarketSource market, IRecipeSource recipes, Options options = null)
    {
        options ??= new Options();
        var (result, _) = await ObtainAsync(tag, quantity, market, recipes, options, 0, new HashSet<string>(), new Dictionary<(string, long), Obtainment>());
        return result;
    }

    /// <summary>
    /// Same as the public overload, but additionally reports whether the result is "exact": context
    /// independent, and therefore safe to memoize/reuse at any depth/stack. A result computed while
    /// crafting was skipped due to the cycle guard (<paramref name="stack"/>) or <see cref="Options.MaxDepth"/>
    /// is context-dependent (a shallower/unstacked evaluation might have crafted instead), so it must
    /// never be memoized or trusted from the memo.
    /// </summary>
    private static async Task<(Obtainment result, bool exact)> ObtainAsync(string tag, long quantity, IMarketSource market, IRecipeSource recipes,
        Options options, int depth, HashSet<string> stack, Dictionary<(string, long), Obtainment> memo)
    {
        if (quantity <= 0)
            return (new Obtainment(0, true, "buy"), true);
        if (tag == "SKYBLOCK_COIN" || tag == "SKYBLOCK_COINS")
            return (new Obtainment(quantity, true, "buy"), true);
        // Premium currencies get a representative coin value instead of the generic unobtainable
        // count*20M fallback, and are flagged with a distinct Method so the parent craft is marked
        // non-normal (see CalculatorService.GetCreaftingCost / CraftsController's profit filters).
        if (tag == "SKYBLOCK_BIT")
            return (new Obtainment(quantity * options.CoinsPerBit, true, "bits"), true);
        if (tag == "SKYBLOCK_COPPER")
            return (new Obtainment(quantity * options.CoinsPerCopper, true, "copper"), true);
        if (tag == "SKYBLOCK_MOTE")
            // Motes are non-transferable and Rift-only: no representative coin value exists, so this
            // stays unobtainable (Enough=false) - same treatment as the generic fallback - but is still
            // tagged distinctly so the parent craft is excluded from the normal profit lists.
            return (new Obtainment(quantity * options.UnmetFallbackUnitPrice, false, "mote"), true);
        if (memo.TryGetValue((tag, quantity), out var cached))
            return (cached, true);

        // Option 1: buy the item on the market (npc + buy orders + insta buy), supply limited.
        var buy = await BuyAsync(tag, quantity, market, options);
        var best = buy;
        // Buying alone is always exact: it has no recursion/context dependence.
        var exact = true;

        // Option 2: craft it, recursively obtaining the ingredients at the quantities actually needed.
        // Evaluate TryGetRecipe unconditionally (independent of depth/stack) so "not craftable" stays exact.
        // directlyCraftable no longer affects pricing (see Options.CraftStepMarkup) - kept on the
        // interface since RecipeSource still computes it from the item's recipe type, but unused here.
        if (recipes.TryGetRecipe(tag, out var ingredients, out var yield, out _) && ingredients.Count > 0)
        {
            if (depth >= options.MaxDepth || stack.Contains(tag))
            {
                // Crafting was skipped purely because of this call's context (cycle guard / depth
                // limit); a shallower/unstacked evaluation of the same (tag, quantity) could still
                // craft, so this buy-only result must not be memoized or reused elsewhere.
                exact = false;
            }
            else
            {
                yield = Math.Max(1, yield);
                var batches = (quantity + yield - 1) / yield;
                // Uniform per-step effort markup for every craft step, direct or indirect (forge, malik,
                // npc_shop, carpentry, ...) - see Options.CraftStepMarkup for why there is no separate
                // multiplier for indirect/time-gated steps.
                var stepFactor = options.CraftStepMarkup;
                // When buying already succeeds, crafting can only win by coming in under this ceiling.
                // craft wins when (craftCost*stepFactor + flat) * margin < best.Cost, i.e.
                // craftCost < (best.Cost/margin - flat) / stepFactor. The running craft cost only grows
                // as ingredients are added, so once it passes the ceiling we can stop recursing the
                // rest: the outcome (buy) is already decided. This is exact - it prunes doomed deep
                // sub-craft recursion without changing any result. When buying can not supply enough
                // there is no ceiling, so every ingredient is still explored.
                double craftCostCeiling;
                if (best.Enough)
                {
                    var marginAdjusted = best.Cost / options.CraftPreferenceMargin - options.CraftStepFlatCoins;
                    // If even zero flat/markup overhead can't beat buying, crafting can never win here.
                    craftCostCeiling = marginAdjusted <= 0 ? 0 : marginAdjusted / stepFactor;
                }
                else
                {
                    craftCostCeiling = double.PositiveInfinity;
                }
                double craftCost = 0;
                var craftViable = true;
                var subsExact = true;
                stack.Add(tag);
                // Recurse the biggest quantities first so an over-budget ingredient trips the ceiling sooner.
                foreach (var ingredient in ingredients.OrderByDescending(i => i.count))
                {
                    if (craftCost >= craftCostCeiling)
                    {
                        // Even ignoring the remaining ingredients, crafting can no longer beat buying.
                        craftViable = false;
                        break;
                    }
                    var (sub, subExact) = await ObtainAsync(ingredient.tag, ingredient.count * batches, market, recipes, options, depth + 1, stack, memo);
                    craftCost += sub.Cost;
                    if (!subExact)
                        subsExact = false;
                    if (!sub.Enough)
                    {
                        // An ingredient can not be sourced in the needed amount, so this craft can not be
                        // completed at scale; crafting is not a valid option.
                        craftViable = false;
                        break;
                    }
                }
                stack.Remove(tag);
                if (craftViable)
                {
                    var effectiveCraftCost = craftCost * stepFactor + options.CraftStepFlatCoins;
                    // Prefer crafting when it is meaningfully cheaper, or when buying can not supply enough.
                    var craftBeatsBuy = effectiveCraftCost * options.CraftPreferenceMargin < best.Cost || !best.Enough;
                    if (craftBeatsBuy)
                    {
                        best = new Obtainment(effectiveCraftCost, true, "craft");
                    }
                    // else: best stays the (exact) buy result.
                }
                // else: craftViable is false because of the cost-ceiling prune or an unmet ingredient;
                // best stays the (exact) buy result.
                // Whenever the craft branch actually ran (i.e. we did not take the depth/stack skip
                // above), any sub-result it consumed - whether craft ultimately won, buy won, or
                // crafting was abandoned via the ceiling prune / an unmet ingredient - taints the
                // outcome the same way: a shallower/unstacked evaluation could resolve differently.
                // This is a no-op (subsExact stays true) when no sub was ever consumed, e.g. an
                // immediate ceiling==0 prune, so genuinely exact "buy dominates by margin" results
                // are still memoized correctly.
                exact = exact && subsExact;
            }
        }

        if (exact)
            memo[(tag, quantity)] = best;
        return (best, exact);
    }

    private static async Task<Obtainment> BuyAsync(string tag, long quantity, IMarketSource market, Options options)
    {
        var tranches = await market.GetBuyTranchesAsync(tag);
        if (tranches == null || tranches.Count == 0)
            return new Obtainment(quantity * options.UnmetFallbackUnitPrice, false, "buy");
        var (filled, unmet, source) = SmartBuyer.Cost(tranches, quantity);
        if (unmet <= 0)
            return new Obtainment(filled, true, MethodFor(source));
        // Could not source everything: price the remainder at a penalty so it does not look cheap.
        var worstPrice = tranches.Where(t => t.Capacity > 0 && t.UnitPrice >= 0)
            .Select(t => t.UnitPrice).DefaultIfEmpty(options.UnmetFallbackUnitPrice).Max();
        if (worstPrice <= 0)
            worstPrice = options.UnmetFallbackUnitPrice;
        var penalized = filled + unmet * worstPrice * options.UnmetPenaltyFactor;
        return new Obtainment(penalized, false, MethodFor(source));
    }

    private static string MethodFor(string source) => source == "npc" ? "npc" : "buy";
}
