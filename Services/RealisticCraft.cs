using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;

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
    /// <summary>How many units can realistically be sourced here (npc stock, ~30 min of insta-sell volume, order book depth).</summary>
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
/// <param name="BuyCost">
/// The cost of obtaining the item purely by BUYING it (npc/order/insta tranches), when that is
/// actually viable (buying could supply the full quantity); 0 when there is no viable buy
/// alternative. Lets callers show how much a chosen craft saves vs. just buying the item, even
/// though <see cref="Cost"/>/<see cref="Method"/> may reflect the cheaper craft path instead.
/// </param>
public record Obtainment(double Cost, bool Enough, string Method, double BuyCost = 0)
{
    public CraftAcquisitionPlan Plan { get; init; }
}

public record PurchaseFillResult(double Cost, long Unmet, string DominantSource, IReadOnlyList<AcquisitionFill> Fills);

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
        var result = Fill(tranches, quantity);
        return (result.Cost, result.Unmet, result.DominantSource);
    }

    public static PurchaseFillResult Fill(IEnumerable<PriceTranche> tranches, long quantity, double maxUnitPrice = double.PositiveInfinity)
    {
        if (quantity <= 0)
            return new PurchaseFillResult(0, 0, "buy", Array.Empty<AcquisitionFill>());
        double cost = 0;
        long remaining = quantity;
        var perSource = new Dictionary<string, long>();
        var fills = new List<AcquisitionFill>();
        foreach (var tranche in tranches.Where(t => t.Capacity > 0 && t.UnitPrice >= 0 && t.UnitPrice < maxUnitPrice).OrderBy(t => t.UnitPrice))
        {
            if (remaining <= 0)
                break;
            var take = Math.Min(remaining, tranche.Capacity);
            cost += take * tranche.UnitPrice;
            remaining -= take;
            fills.Add(new AcquisitionFill { Source = tranche.Source, Quantity = take, UnitPrice = tranche.UnitPrice, Cost = take * tranche.UnitPrice });
            perSource.TryGetValue(tranche.Source, out var existing);
            perSource[tranche.Source] = existing + take;
        }
        var dominant = perSource.Count == 0 ? "buy" : perSource.OrderByDescending(p => p.Value).First().Key;
        return new PurchaseFillResult(cost, Math.Max(0, remaining), dominant, fills);
    }

    /// <summary>
    /// Summarizes <paramref name="tranches"/> into the per-channel numbers a frontend acquisition
    /// breakdown needs: how many units come from npc stock and at what price, how many from a competitive
    /// bazaar buy order and at what price, and the capacity/average cost of the standing sell offers. The
    /// npc and "order" buckets are kept SEPARATE (they were merged in an earlier version) so the frontend can
    /// show a per-channel breakdown ("640 from npc, 27 119 buy-ordered, the rest insta-bought"). Mirrors the
    /// same tranche filter <see cref="Cost"/> uses (Capacity > 0 and UnitPrice >= 0) so the two stay consistent.
    /// </summary>
    /// <returns>
    /// npcCapacity: units obtainable from npc stock; npcUnitPrice: capacity-weighted avg npc unit price (0 if none);
    /// orderCapacity: units obtainable via competitive buy orders; orderUnitPrice: capacity-weighted avg buy-order
    /// unit price (0 if none); instaCapacity: units available from standing sell offers; instaUnitPrice:
    /// capacity-weighted avg insta unit price (0 if there are no insta tranches).
    /// </returns>
    public static (long npcCapacity, double npcUnitPrice, long orderCapacity, double orderUnitPrice, long instaCapacity, double instaUnitPrice) SummarizeTranches(IEnumerable<PriceTranche> tranches)
    {
        long npcCapacity = 0;
        double npcWeightedCost = 0;
        long orderCapacity = 0;
        double orderWeightedCost = 0;
        long instaCapacity = 0;
        double instaWeightedCost = 0;
        foreach (var tranche in tranches.Where(t => t.Capacity > 0 && t.UnitPrice >= 0))
        {
            if (tranche.Source == "npc")
            {
                npcCapacity += tranche.Capacity;
                npcWeightedCost += tranche.UnitPrice * tranche.Capacity;
            }
            else if (tranche.Source == "order")
            {
                orderCapacity += tranche.Capacity;
                orderWeightedCost += tranche.UnitPrice * tranche.Capacity;
            }
            else if (tranche.Source == "insta")
            {
                instaCapacity += tranche.Capacity;
                instaWeightedCost += tranche.UnitPrice * tranche.Capacity;
            }
        }
        var npcUnitPrice = npcCapacity > 0 ? npcWeightedCost / npcCapacity : 0;
        var orderUnitPrice = orderCapacity > 0 ? orderWeightedCost / orderCapacity : 0;
        var instaUnitPrice = instaCapacity > 0 ? instaWeightedCost / instaCapacity : 0;
        return (npcCapacity, npcUnitPrice, orderCapacity, orderUnitPrice, instaCapacity, instaUnitPrice);
    }
}

/// <summary>Provides the price tranches an item can be bought from (npc + market).</summary>
public interface IMarketSource
{
    /// <summary>
    /// Returns up to <paramref name="quantity"/> additional units of <paramref name="tag"/>, after the market
    /// depth in <paramref name="alreadyLoaded"/>. Auction implementations use that loaded depth to request
    /// the correct cumulative quantity and return only its marginal cost.
    /// The underlying market data (bazaar batch + order books) this is built from is fetched once
    /// per pricing pass and cached by the implementation; the per-tag tranches themselves are cheap
    /// to recompute and are NOT cached, so this may be called repeatedly for the same tag.
    /// </summary>
    Task<IReadOnlyList<PriceTranche>> GetBuyTranchesAsync(string tag, long quantity, IReadOnlyList<PriceTranche> alreadyLoaded);
}

/// <summary>One candidate recipe: the ingredients needed for one batch and the batch's yield.</summary>
public readonly record struct RecipeOption(IReadOnlyList<(string tag, long count)> Ingredients, long Yield);

/// <summary>Provides crafting recipes so the realistic calculator can expand sub-crafts.</summary>
public interface IRecipeSource
{
    /// <summary>
    /// Gets ALL candidate recipes for <paramref name="tag"/> if it is craftable, so the caller can
    /// evaluate each and keep the cheapest. directlyCraftable is not exposed here - it does not affect
    /// pricing (see Options.CraftStepMarkup).
    /// </summary>
    bool TryGetRecipes(string tag, out IReadOnlyList<RecipeOption> recipes);
}

/// <summary>
/// Computes the realistic cost to obtain a quantity of an item, choosing between buying it and
/// crafting it (recursively) using quantity aware, supply limited market pricing. Because it works
/// with the true quantities needed (e.g. ~1M obsidian for one end game weapon), sub-crafts that only
/// look cheap while ignoring npc stock and bazaar volume correctly stop being chosen at scale.
/// </summary>
public static class RealisticCraft
{
    private sealed class SupplyPool
    {
        private readonly IMarketSource market;
        private Dictionary<string, List<PriceTranche>> remaining;
        private Dictionary<string, List<PriceTranche>> loaded;

        public SupplyPool(IMarketSource market)
        {
            this.market = market;
            remaining = new Dictionary<string, List<PriceTranche>>(StringComparer.Ordinal);
            loaded = new Dictionary<string, List<PriceTranche>>(StringComparer.Ordinal);
        }

        private SupplyPool(IMarketSource market, Dictionary<string, List<PriceTranche>> remaining,
            Dictionary<string, List<PriceTranche>> loaded)
        {
            this.market = market;
            this.remaining = remaining;
            this.loaded = loaded;
        }

        public SupplyPool Fork() => new(market,
            Clone(remaining), Clone(loaded));

        public void Commit(SupplyPool selected)
        {
            var selectedCopy = selected.Fork();
            remaining = selectedCopy.remaining;
            loaded = selectedCopy.loaded;
        }

        public async Task<IReadOnlyList<PriceTranche>> GetAsync(string tag, long quantity)
        {
            if (!remaining.TryGetValue(tag, out var tranches))
            {
                tranches = new List<PriceTranche>();
                remaining[tag] = tranches;
            }
            if (!loaded.TryGetValue(tag, out var loadedTranches))
            {
                loadedTranches = new List<PriceTranche>();
                loaded[tag] = loadedTranches;
            }
            var missing = quantity - tranches.Sum(t => t.Capacity);
            if (missing > 0)
            {
                var source = await market.GetBuyTranchesAsync(tag, missing, loadedTranches) ?? Array.Empty<PriceTranche>();
                foreach (var tranche in source.Where(t => t.Capacity > 0))
                {
                    var copy = new PriceTranche(tranche.UnitPrice, tranche.Capacity, tranche.Source);
                    tranches.Add(copy);
                    loadedTranches.Add(copy);
                }
            }
            return tranches;
        }

        private static Dictionary<string, List<PriceTranche>> Clone(Dictionary<string, List<PriceTranche>> source)
            => source.ToDictionary(pair => pair.Key,
                pair => pair.Value.Select(t => new PriceTranche(t.UnitPrice, t.Capacity, t.Source)).ToList(),
                StringComparer.Ordinal);

        public void Consume(string tag, IEnumerable<AcquisitionFill> fills)
        {
            if (!remaining.TryGetValue(tag, out var tranches))
                return;
            foreach (var fill in fills)
            {
                var left = fill.Quantity;
                for (var i = 0; i < tranches.Count && left > 0; i++)
                {
                    var tranche = tranches[i];
                    if (tranche.Source != fill.Source || tranche.UnitPrice != fill.UnitPrice || tranche.Capacity <= 0)
                        continue;
                    var consumed = Math.Min(left, tranche.Capacity);
                    tranches[i] = new PriceTranche(tranche.UnitPrice, tranche.Capacity - consumed, tranche.Source);
                    left -= consumed;
                }
            }
        }
    }

    /// <summary>
    /// Maximum units of one item that can be put into a single bazaar buy order (Hypixel's cap on one
    /// order). Lives here, next to the pricing logic that reasons about it, and is reused by
    /// CalculatorService to cap the "order" price tranche capacity so the two never drift apart.
    /// </summary>
    public const long MaxSingleOrderQuantity = 71_000;

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
        /// <summary>
        /// Maximum units of one item that can be put into a single bazaar buy order. Larger amounts need
        /// multiple sequential orders, which is what <see cref="BulkCraftStepMarkup"/> prices in. Overridable
        /// per pricing run; defaults to the shared <see cref="RealisticCraft.MaxSingleOrderQuantity"/> that
        /// CalculatorService also caps the "order" price tranche with.
        /// </summary>
        public long MaxSingleOrderQuantity { get; set; } = RealisticCraft.MaxSingleOrderQuantity;
        /// <summary>
        /// Craft-step markup used instead of CraftStepMarkup when a craft step needs more of an ingredient
        /// than fits in one bazaar order (MaxSingleOrderQuantity), since it takes multiple sequential orders
        /// and moves the price.
        /// </summary>
        public double BulkCraftStepMarkup { get; set; } = 1.20;
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
        /// <summary>Include the selected quantity-specific acquisition tree in the result.</summary>
        public bool BuildPlan { get; set; }
    }

    /// <summary>
    /// Figures out the cheapest realistic way to obtain <paramref name="quantity"/> of <paramref name="tag"/>.
    /// </summary>
    public static async Task<Obtainment> ObtainAsync(string tag, long quantity, IMarketSource market, IRecipeSource recipes, Options options = null)
    {
        options ??= new Options();
        var (result, _) = await ObtainAsync(tag, quantity, market, recipes, options, 0, new HashSet<string>(), new Dictionary<(string, long), Obtainment>(), new SupplyPool(market), false, false, true);
        return result;
    }

    /// <summary>
    /// Same as the public overload, but accepts an externally-supplied memo so a single pricing pass
    /// can share sub-craft results (e.g. ENCHANTED_GOLD, ENCHANTED_IRON) across many parent items
    /// instead of recomputing them from scratch per item. Only safe to share within a single pass
    /// where every priced item sees the same frozen market data (bazaar batch, CoinsPerBit,
    /// CoinsPerCopper) - the memo must be cleared/replaced at the start of each new pass.
    /// </summary>
    public static async Task<Obtainment> ObtainAsync(string tag, long quantity, IMarketSource market, IRecipeSource recipes, Options options, IDictionary<(string, long), Obtainment> memo)
    {
        options ??= new Options();
        var (result, _) = await ObtainAsync(tag, quantity, market, recipes, options, 0, new HashSet<string>(), memo, new SupplyPool(market), false, false, true);
        return result;
    }

    public static async Task<IReadOnlyList<Obtainment>> ObtainAllAsync(IReadOnlyList<(string tag, long quantity)> requests,
        IMarketSource market, IRecipeSource recipes, Options options = null)
    {
        options ??= new Options();
        var supply = new SupplyPool(market);
        var results = new List<Obtainment>(requests.Count);
        foreach (var request in requests)
        {
            var (result, _) = await ObtainAsync(request.tag, request.quantity, market, recipes, options, 0,
                new HashSet<string>(), new Dictionary<(string, long), Obtainment>(), supply, true, false, true);
            results.Add(result);
        }
        return results;
    }

    public static async Task<Obtainment> ObtainCraftAsync(string tag, long quantity, IMarketSource market, IRecipeSource recipes, Options options = null)
    {
        options ??= new Options();
        options.BuildPlan = true;
        var (result, _) = await ObtainAsync(tag, quantity, market, recipes, options, 0, new HashSet<string>(), new Dictionary<(string, long), Obtainment>(), new SupplyPool(market), true, true, false);
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
        Options options, int depth, HashSet<string> stack, IDictionary<(string, long), Obtainment> memo, SupplyPool supply,
        bool statefulSupply, bool forceCraft, bool allowHybrid)
    {
        if (quantity <= 0)
            return (new Obtainment(0, true, "buy"), true);
        if (tag == "SKYBLOCK_COIN" || tag == "SKYBLOCK_COINS")
            return (new Obtainment(quantity, true, "buy", quantity), true);
        // Premium currencies get a representative coin value instead of the generic unobtainable
        // count*20M fallback, and are flagged with a distinct Method so the parent craft is marked
        // non-normal (see CalculatorService.GetCreaftingCost / CraftsController's profit filters).
        // These are acquired by "buying" (spending bits/copper), so BuyCost equals Cost.
        if (tag == "SKYBLOCK_BIT")
        {
            var cost = quantity * options.CoinsPerBit;
            return (new Obtainment(cost, true, "bits", cost), true);
        }
        if (tag == "SKYBLOCK_COPPER")
        {
            var cost = quantity * options.CoinsPerCopper;
            return (new Obtainment(cost, true, "copper", cost), true);
        }
        if (tag == "SKYBLOCK_MOTE")
            // Motes are non-transferable and Rift-only: no representative coin value exists, so this
            // stays unobtainable (Enough=false) - same treatment as the generic fallback - but is still
            // tagged distinctly so the parent craft is excluded from the normal profit lists.
            return (new Obtainment(quantity * options.UnmetFallbackUnitPrice, false, "mote"), true);
        if (!statefulSupply && !options.BuildPlan && !forceCraft && memo.TryGetValue((tag, quantity), out var cached))
            return (cached, true);

        // Option 1: buy the item on the market (npc + buy orders + insta buy), supply limited.
        var tranches = await supply.GetAsync(tag, quantity);
        var buySupply = supply.Fork();
        var buy = Buy(tag, quantity, tranches, options, options.BuildPlan || statefulSupply);
        buySupply.Consume(tag, buy.Plan?.Purchases ?? Array.Empty<AcquisitionFill>());
        var best = buy;
        var bestSupply = buySupply;
        Obtainment bestCraft = null;
        SupplyPool bestCraftSupply = null;
        // Buying alone is always exact: it has no recursion/context dependence.
        var exact = true;
        // The genuine cost of buying this item outright, kept alongside whatever ends up cheapest
        // (craft or buy) so callers can show how much a chosen craft saves vs. the buy alternative.
        // 0 when buying could not supply the full quantity (no viable buy alternative).
        var buyAlternative = buy.Enough ? buy.Cost : 0;

        // Option 2: craft it, recursively obtaining the ingredients at the quantities actually needed.
        // Evaluate TryGetRecipes unconditionally (independent of depth/stack) so "not craftable" stays exact.
        // Some items have multiple recipes (e.g. ENCHANTED_GOLD via GOLD_INGOT or via GOLD_BLOCK) -
        // every candidate is evaluated and the cheapest realistic one wins, order-independently.
        // directlyCraftable no longer affects pricing (see Options.CraftStepMarkup), so it is not part
        // of the candidate shape at all.
        if (recipes.TryGetRecipes(tag, out var candidates) && candidates.Count > 0)
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
                var subsExact = true;
                stack.Add(tag);
                foreach (var candidate in candidates)
                {
                    var candidateSupply = supply.Fork();
                    var ingredients = candidate.Ingredients;
                    if (ingredients == null || ingredients.Count == 0)
                        continue;
                    var yield = Math.Max(1, candidate.Yield);
                    var batches = (quantity + yield - 1) / yield;
                    // A single bazaar order tops out at MaxSingleOrderQuantity units, so a step needing more of any
                    // one ingredient takes several sequential orders (and moves the price) - price that extra effort
                    // with the higher bulk markup.
                    var needsBulkOrdering = ingredients.Any(i => i.count * batches > options.MaxSingleOrderQuantity);
                    var stepFactor = needsBulkOrdering ? options.BulkCraftStepMarkup : options.CraftStepMarkup;
                    // When buying (or a cheaper candidate found earlier) already succeeds, this candidate
                    // can only win by coming in under this ceiling. craft wins when
                    // (craftCost*stepFactor + flat) * margin < best.Cost, i.e.
                    // craftCost < (best.Cost/margin - flat) / stepFactor. The running craft cost only grows
                    // as ingredients are added, so once it passes the ceiling we can stop recursing the
                    // rest: the outcome (best-so-far) is already decided for this candidate. This is exact -
                    // it prunes doomed deep sub-craft recursion without changing any result. Recomputed from
                    // the CURRENT best at the start of each candidate, so a cheaper candidate found earlier
                    // tightens the ceiling for later ones. When buying can not supply enough there is no
                    // ceiling, so every ingredient is still explored.
                    double craftCostCeiling;
                    if (best.Enough && !options.BuildPlan)
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
                    double planCraftCost = 0;
                    var craftViable = true;
                    var childPlans = options.BuildPlan ? new List<CraftAcquisitionPlan>() : null;
                    // Recurse the biggest quantities first so an over-budget ingredient trips the ceiling sooner.
                    foreach (var ingredient in ingredients.OrderByDescending(i => i.count))
                    {
                        if (craftCost >= craftCostCeiling)
                        {
                            // Even ignoring the remaining ingredients, this candidate can no longer beat best.
                            craftViable = false;
                            break;
                        }
                        var (sub, subExact) = await ObtainAsync(ingredient.tag, ingredient.count * batches, market, recipes, options,
                            depth + 1, stack, memo, candidateSupply, statefulSupply, false, true);
                        craftCost += sub.Cost;
                        planCraftCost += sub.Plan?.Cost ?? sub.Cost;
                        if (sub.Plan != null)
                            childPlans?.Add(sub.Plan);
                        if (!subExact)
                            subsExact = false;
                        if (!sub.Enough)
                        {
                            // An ingredient can not be sourced in the needed amount, so this candidate can
                            // not be completed at scale; it is not a valid option.
                            craftViable = false;
                            break;
                        }
                    }
                    if (craftViable)
                    {
                        var effectiveCraftCost = craftCost * stepFactor + options.CraftStepFlatCoins;
                        var craftResult = new Obtainment(effectiveCraftCost, true, "craft")
                        {
                            Plan = options.BuildPlan ? new CraftAcquisitionPlan
                            {
                                ItemId = tag,
                                Quantity = quantity,
                                Cost = planCraftCost,
                                Enough = true,
                                Method = "craft",
                                CraftedQuantity = quantity,
                                Ingredients = childPlans
                            } : null
                        };
                        if (bestCraft == null || craftResult.Cost < bestCraft.Cost)
                        {
                            bestCraft = craftResult;
                            bestCraftSupply = candidateSupply;
                        }
                        // Prefer this candidate when it is meaningfully cheaper than the current best, or
                        // when the current best can not supply enough.
                        var craftBeatsBest = effectiveCraftCost * options.CraftPreferenceMargin < best.Cost || !best.Enough;
                        if (craftBeatsBest)
                        {
                            best = craftResult;
                            bestSupply = candidateSupply;
                        }
                    }
                    // else: candidate is not viable (ceiling prune or unmet ingredient); best is unchanged
                    // and the next candidate is still evaluated.
                }
                stack.Remove(tag);
                // Whenever the craft branch actually ran (i.e. we did not take the depth/stack skip
                // above), any sub-result any candidate consumed - whether craft ultimately won, buy won,
                // or a candidate was abandoned via the ceiling prune / an unmet ingredient - taints the
                // outcome the same way: a shallower/unstacked evaluation could resolve differently. This
                // is a no-op (subsExact stays true) when no sub was ever consumed, so genuinely exact
                // "buy dominates by margin" results are still memoized correctly.
                exact = exact && subsExact;
            }
        }

        if (options.BuildPlan)
        {
            if (forceCraft)
            {
                best = bestCraft ?? buy;
                bestSupply = bestCraftSupply ?? buySupply;
            }
            else
            {
                best = buy;
                bestSupply = buySupply;
                var bestScore = buy.Enough ? buy.Cost : double.PositiveInfinity;
                if (bestCraft?.Enough == true && bestCraft.Cost * options.CraftPreferenceMargin < bestScore)
                {
                    best = bestCraft;
                    bestSupply = bestCraftSupply;
                    bestScore = bestCraft.Cost * options.CraftPreferenceMargin;
                }

                if (allowHybrid && bestCraft?.Enough == true)
                {
                    var hybridSupply = supply.Fork();
                    var hybridTranches = await hybridSupply.GetAsync(tag, quantity);
                    var cheapPurchases = SmartBuyer.Fill(hybridTranches, quantity, bestCraft.Cost / quantity);
                    hybridSupply.Consume(tag, cheapPurchases.Fills);
                    if (cheapPurchases.Unmet > 0 && cheapPurchases.Unmet < quantity)
                    {
                        var (remainderCraft, remainderExact) = await ObtainAsync(tag, cheapPurchases.Unmet, market, recipes, options,
                            depth, stack, memo, hybridSupply, statefulSupply, true, false);
                        exact &= remainderExact;
                        var hybridScore = cheapPurchases.Cost + remainderCraft.Cost * options.CraftPreferenceMargin;
                        if (remainderCraft.Enough && hybridScore < bestScore)
                        {
                            var hybridCost = cheapPurchases.Cost + remainderCraft.Cost;
                            var planCost = cheapPurchases.Cost + (remainderCraft.Plan?.Cost ?? remainderCraft.Cost);
                            best = new Obtainment(hybridCost, true, "craft")
                            {
                                Plan = new CraftAcquisitionPlan
                                {
                                    ItemId = tag,
                                    Quantity = quantity,
                                    Cost = planCost,
                                    Enough = true,
                                    Method = "craft",
                                    CraftedQuantity = cheapPurchases.Unmet,
                                    Purchases = cheapPurchases.Fills,
                                    Ingredients = remainderCraft.Plan?.Ingredients ?? Array.Empty<CraftAcquisitionPlan>()
                                }
                            };
                            bestSupply = hybridSupply;
                        }
                    }
                }
            }

            if (best.Plan != null)
            {
                best.Plan.DirectBuyCost = buy.Cost;
                best.Plan.DirectBuyEnough = buy.Enough;
                best.Plan.CraftCost = bestCraft?.Plan?.Cost ?? bestCraft?.Cost ?? 0;
                best.Plan.CraftEnough = bestCraft?.Enough == true;
            }
        }

        best = best with { BuyCost = buyAlternative };
        supply.Commit(bestSupply);
        if (exact && !statefulSupply && !options.BuildPlan && !forceCraft)
            memo[(tag, quantity)] = best;
        return (best, exact);
    }

    private static Obtainment Buy(string tag, long quantity, IReadOnlyList<PriceTranche> tranches, Options options, bool includePlan)
    {
        if (tranches == null || tranches.Count == 0)
            return new Obtainment(quantity * options.UnmetFallbackUnitPrice, false, "buy");
        var purchase = SmartBuyer.Fill(tranches, quantity);
        var filled = purchase.Cost;
        var unmet = purchase.Unmet;
        var source = purchase.DominantSource;
        if (unmet <= 0)
            return new Obtainment(filled, true, MethodFor(source))
            {
                Plan = includePlan ? new CraftAcquisitionPlan
                {
                    ItemId = tag,
                    Quantity = quantity,
                    Cost = filled,
                    Enough = true,
                    Method = MethodFor(source),
                    Purchases = purchase.Fills
                } : null
            };
        // Could not source everything: price the remainder at a penalty so it does not look cheap.
        var worstPrice = tranches.Where(t => t.Capacity > 0 && t.UnitPrice >= 0)
            .Select(t => t.UnitPrice).DefaultIfEmpty(options.UnmetFallbackUnitPrice).Max();
        if (worstPrice <= 0)
            worstPrice = options.UnmetFallbackUnitPrice;
        var penalized = filled + unmet * worstPrice * options.UnmetPenaltyFactor;
        return new Obtainment(penalized, false, MethodFor(source))
        {
            Plan = includePlan ? new CraftAcquisitionPlan
            {
                ItemId = tag,
                Quantity = quantity,
                Cost = penalized,
                Enough = false,
                Method = MethodFor(source),
                Purchases = purchase.Fills
            } : null
        };
    }

    private static string MethodFor(string source) => source == "npc" ? "npc" : "buy";
}
