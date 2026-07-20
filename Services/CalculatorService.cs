using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SkyCrafts.Tests")]

namespace Coflnet.Sky.Crafts.Services
{
    public class CalculatorService
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly IReadOnlyDictionary<string, double> HardcodedSourceCosts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            // Hypixel exposes the Cheap Tuxedo as a 3m set in the UI, so each of the three pieces
            // needs a fixed 1m source cost until the per-piece pricing is available automatically.
            ["CHEAP_TUXEDO_BOOTS"] = 1_000_000,
            ["CHEAP_TUXEDO_CHESTPLATE"] = 1_000_000,
            ["CHEAP_TUXEDO_LEGGINGS"] = 1_000_000,
        };
        /// <summary>
        /// Fallback per-restock npc stock used when the shop stock is unknown. Most npc shops cap the
        /// obtainable amount, so a large craft can not source unlimited base materials at npc prices.
        /// </summary>
        public const int DefaultNpcStock = 640;
        /// <summary>Ingredient/craft Type markers used for the SKYBLOCK_BIT/COPPER/MOTE pseudo-currencies.</summary>
        private static readonly HashSet<string> CurrencyIngredientTypes = new(StringComparer.Ordinal) { "bits", "copper", "mote" };
        /// <summary>Number of lowest bins sampled to estimate the per-unit price and depth of auction house items.</summary>
        private const int AhSampleCount = 64;
        /// <summary>Hours of insta-sell volume assumed to fill a buy order in reasonable time.</summary>
        private const double BuyOrderFillHours = 0.5;
        /// <summary>
        /// Coins added on top of the current top buy order price to actually place a competitive order:
        /// you must sit just above the existing top order to have any chance of it filling.
        /// </summary>
        private const double BuyOrderOutbidCoins = 0.2;
        /// <summary>
        /// Multiplier applied on top of the outbid price to account for the buy order being time-gated -
        /// it fills over ~<see cref="BuyOrderFillHours"/> hours, tying up capital and risking the price
        /// moving before it fills, so its effective cost carries a premium over an instant purchase.
        /// </summary>
        private const double BuyOrderTimeGateMarkup = 1.20;

        /// <summary>Fallback coinsPerBit used when the live booster-cookie bazaar lookup fails (see Options.CoinsPerBit).</summary>
        private const double DefaultCoinsPerBit = 2000;
        /// <summary>Fallback coinsPerCopper used when the live sky-bazaar-flipper lookup fails (see Options.CoinsPerCopper).</summary>
        private const double DefaultCoinsPerCopper = 2000;
        /// <summary>Base bits granted by consuming a single Booster Cookie (before the Bits Multiplier).</summary>
        private const double BoosterCookieBaseBits = 4800;
        /// <summary>Bits Multiplier at fame rank "Settler" (2nd stage) - most bits are created here.</summary>
        private const double RepresentativeBitsMultiplier = 1.1;
        /// <summary>Representative bits produced by one Booster Cookie, assuming the 2nd-stage Bits Multiplier.</summary>
        private const double BitsPerBoosterCookie = BoosterCookieBaseBits * RepresentativeBitsMultiplier; // 5280
        /// <summary>Bazaar item tag for Booster Cookie, the item consumed to create bits.</summary>
        private const string BoosterCookieTag = "BOOSTER_COOKIE";

        private IConfiguration config;
        private IItemsApi playerItemsApi;
        private IBazaarApi bazaarApi;
        private IOrderBookApi orderBookApi;
        private IBazaarFlipperApi bazaarFlipperApi;
        private Dictionary<string, double> npcCosts;
        private Dictionary<string, int> npcStock;
        // The whole bazaar (summary prices + full order books) is pulled in two batch calls per pass
        // and shared across every item and recursion level, so pricing is just dictionary lookups.
        private readonly object bazaarBatchLock = new();
        private Task<BazaarBatch> bazaarBatchTask;
        private readonly ConcurrentDictionary<string, Task<PriceResponse>> ahPriceCache = new();
        // Shared realistic-craft memo for the current pricing pass: every item in one pass prices
        // against the same frozen bazaar batch / CoinsPerBit / CoinsPerCopper, so a given (tag,
        // quantity) has an identical cost for every item - safe to reuse across items, not across
        // passes (cleared in ResetPriceCaches). ConcurrentDictionary because both the pass itself
        // (Parallel.ForEachAsync) and PriceIngredientsAsync (Task.WhenAll) recompute concurrently.
        private readonly ConcurrentDictionary<(string, long), Obtainment> craftCostMemo = new();
        // The bit/copper live rates are fetched once per pricing pass (like the bazaar batch) and shared.
        private readonly object coinsPerBitLock = new();
        private Task<double> coinsPerBitTask;
        private readonly object coinsPerCopperLock = new();
        private Task<double> coinsPerCopperTask;
        private bool loggedCoinsPerBitFallback;
        private bool loggedCoinsPerCopperFallback;

        public CalculatorService(IConfiguration config, IItemsApi playerItemsApi, IBazaarApi bazaarApi = null, IOrderBookApi orderBookApi = null,
            IBazaarFlipperApi bazaarFlipperApi = null)
        {
            this.config = config;
            this.playerItemsApi = playerItemsApi;
            this.bazaarApi = bazaarApi;
            this.orderBookApi = orderBookApi;
            this.bazaarFlipperApi = bazaarFlipperApi;
        }

        /// <summary>
        /// Drops the cached market snapshots so the next iteration prices against fresh order books.
        /// Npc costs are intentionally kept (they change rarely).
        /// </summary>
        public void ResetPriceCaches()
        {
            lock (bazaarBatchLock)
                bazaarBatchTask = null;
            lock (coinsPerBitLock)
                coinsPerBitTask = null;
            lock (coinsPerCopperLock)
                coinsPerCopperTask = null;
            ahPriceCache.Clear();
            craftCostMemo.Clear();
        }

        /// <summary>Returns the shared coinsPerBit rate for this pass, fetching it once on first use.</summary>
        internal Task<double> GetCoinsPerBitAsync(HashSet<string> bazaarItems)
        {
            if (coinsPerBitTask != null)
                return coinsPerBitTask;
            lock (coinsPerBitLock)
                coinsPerBitTask ??= FetchCoinsPerBitAsync(bazaarItems);
            return coinsPerBitTask;
        }

        private async Task<double> FetchCoinsPerBitAsync(HashSet<string> bazaarItems)
        {
            try
            {
                // Reuse the already-fetched bazaar batch (no second bazaar fetch) for the buy price.
                // Bits are created by consuming Booster Cookies, so the coin value of one bit is the
                // live acquisition cost of a Booster Cookie divided by the bits it yields.
                var batch = await GetBazaarBatchAsync(bazaarItems);
                if (!batch.Prices.TryGetValue(BoosterCookieTag, out var price) || price.BuyPrice <= 0)
                    throw new InvalidOperationException("No booster cookie bazaar price resolved");
                return price.BuyPrice / BitsPerBoosterCookie;
            }
            catch (Exception e)
            {
                if (!loggedCoinsPerBitFallback)
                {
                    loggedCoinsPerBitFallback = true;
                    Console.WriteLine($"Falling back to default coinsPerBit ({DefaultCoinsPerBit}): {e.Message}");
                }
                return DefaultCoinsPerBit;
            }
        }

        /// <summary>Returns the shared coinsPerCopper rate for this pass, fetching it once on first use.</summary>
        internal Task<double> GetCoinsPerCopperAsync()
        {
            if (coinsPerCopperTask != null)
                return coinsPerCopperTask;
            lock (coinsPerCopperLock)
                coinsPerCopperTask ??= FetchCoinsPerCopperAsync();
            return coinsPerCopperTask;
        }

        private async Task<double> FetchCoinsPerCopperAsync()
        {
            try
            {
                if (bazaarFlipperApi == null)
                    throw new InvalidOperationException("No IBazaarFlipperApi configured");
                var flips = await bazaarFlipperApi.CopperGetAsync();
                // coinsPerCopper = cheapest acquisition cost of one copper = MIN over flips of
                // (buyPrice+analyzeCost)/copperYield = 1/MAX(CopperPerCoin).
                var bestCopperPerCoin = 0d;
                foreach (var flip in flips ?? new List<Bazaar.Flipper.Client.Model.CopperFlip>())
                {
                    if (flip.CopperPerCoin > bestCopperPerCoin)
                        bestCopperPerCoin = flip.CopperPerCoin;
                }
                if (bestCopperPerCoin <= 0)
                    throw new InvalidOperationException("No copper flip resolved to a usable rate");
                return 1.0 / bestCopperPerCoin;
            }
            catch (Exception e)
            {
                if (!loggedCoinsPerCopperFallback)
                {
                    loggedCoinsPerCopperFallback = true;
                    Console.WriteLine($"Falling back to default coinsPerCopper ({DefaultCoinsPerCopper}): {e.Message}");
                }
                return DefaultCoinsPerCopper;
            }
        }

        /// <summary>A single batch snapshot of the bazaar shared across an entire pricing pass.</summary>
        private sealed record BazaarBatch(
            Dictionary<string, Bazaar.Client.Model.OrderBook> Books,
            Dictionary<string, Bazaar.Client.Model.ItemPrice> Prices);

        public async Task<Dictionary<string, double>> GetNpcCosts()
        {
            if (npcCosts != null)
                return npcCosts;
            var allItems = await playerItemsApi.ApiItemsNpccostGetAsync();
            var costs = new Dictionary<string, double>();
            var stock = new Dictionary<string, int>();
            foreach (var item in allItems)
            {
                if (item.Costs == null || !item.Costs.ContainsKey("Coins") || item.Costs.Count != 1)
                    continue; // only consider pure coin costs
                if (item.ResultCount <= 0)
                    continue;
                var perUnitCost = (double)item.Costs["Coins"] / item.ResultCount;
                if (!costs.TryGetValue(item.ItemTag, out var existing) || perUnitCost < existing)
                {
                    costs[item.ItemTag] = perUnitCost;
                    stock[item.ItemTag] = item.Stock;
                }
            }
            foreach (var sourceCost in HardcodedSourceCosts)
            {
                if (!costs.TryGetValue(sourceCost.Key, out var existing) || sourceCost.Value < existing)
                    costs[sourceCost.Key] = sourceCost.Value;
            }
            npcStock = stock;
            npcCosts = costs;
            return npcCosts;
        }

        public async Task<ProfitableCraft> GetCreaftingCost(ItemData item, Dictionary<string, ProfitableCraft> crafts, Dictionary<string, ItemData> lookup, HashSet<string> bazaarItems)
        {
            var candidates = EnumerateRecipeCandidates(item).ToList();
            var sellPriceTask = GetPriceFor(item.internalname, 1);
            var market = new MarketSource(this, bazaarItems);
            var recipeSource = new RecipeSource(this, lookup);
            var options = new RealisticCraft.Options();
            var coinsPerBitTask = GetCoinsPerBitAsync(bazaarItems);
            var coinsPerCopperTask = GetCoinsPerCopperAsync();
            await Task.WhenAll(coinsPerBitTask, coinsPerCopperTask);
            options.CoinsPerBit = coinsPerBitTask.Result;
            options.CoinsPerCopper = coinsPerCopperTask.Result;
            var result = new ProfitableCraft();
            result.ItemId = item.internalname;
            result.ItemName = item.displayname;

            if (candidates.Count == 0)
            {
                // No recipe at all: match the previous NeedCount()-empty behavior (zero-cost craft, no
                // ingredients, recipe type resolved from the raw item data).
                var emptyIngredients = new List<Ingredient>();
                result.Ingredients = emptyIngredients;
                result.CraftCost = 0;
                result.BuyOrderCraftCost = 0;
                var emptyPrice = await sellPriceTask;
                result.SellPrice = bazaarItems.Contains(result.ItemId) ? emptyPrice.BuyPrice - 0.1 : emptyPrice.SellPrice;
                result.Type = ResolveCraftType(item.recipes?.FirstOrDefault()?.type, emptyIngredients);
                return result;
            }

            // Price every candidate recipe (there are only ever a handful per item) and keep the one
            // with the lowest per-unit cost - this is what lets the engine prefer e.g. GOLD_INGOT over
            // GOLD_BLOCK for ENCHANTED_GOLD instead of blindly taking the structurally-first recipe.
            (List<Ingredient> ingredients, long yield, string recipeType) chosen = default;
            var chosenPerUnitCost = double.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                var totalCost = await PriceIngredientsAsync(candidate.ingredients, market, recipeSource, options, craftCostMemo);
                var perUnitCost = totalCost / Math.Max(1, candidate.yield);
                if (perUnitCost < chosenPerUnitCost)
                {
                    chosenPerUnitCost = perUnitCost;
                    chosen = candidate;
                }
            }

            result.CraftCost = chosenPerUnitCost;
            result.BuyOrderCraftCost = chosenPerUnitCost;
            result.Ingredients = chosen.ingredients;
            var itemPrice = await sellPriceTask;
            result.SellPrice = bazaarItems.Contains(result.ItemId) ? itemPrice.BuyPrice - 0.1 : itemPrice.SellPrice;
            result.Type = ResolveCraftType(chosen.recipeType, chosen.ingredients);
            return result;
        }

        /// <summary>
        /// Prices every ingredient in one candidate recipe via the realistic craft engine, mutating each
        /// ingredient's Cost/BuyOrderCost/CraftCost/Type in place exactly as the top-level craft cost did
        /// inline before candidates existed, and returns the summed ingredient cost for the batch.
        /// </summary>
        internal static async Task<double> PriceIngredientsAsync(List<Ingredient> ingredients, IMarketSource market, IRecipeSource recipeSource, RealisticCraft.Options options, IDictionary<(string, long), Obtainment> memo)
        {
            // Obtain each direct ingredient realistically. This recurses into sub-crafts using the true
            // quantities needed for one batch of this recipe, so npc stock and bazaar volume limits are
            // respected even for base materials that are only cheap in small amounts.
            await Task.WhenAll(ingredients.Select(async ingredient =>
            {
                if (ingredient.ItemId == "SKYBLOCK_COIN")
                {
                    ingredient.Cost = ingredient.Count;
                    ingredient.BuyOrderCost = ingredient.Count;
                    return;
                }
                var obtainment = await RealisticCraft.ObtainAsync(ingredient.ItemId, ingredient.Count, market, recipeSource, options, memo);
                ingredient.Cost = obtainment.Cost;
                // The genuine cost of buying this ingredient outright, so downstream can show how much
                // crafting it saved vs. the buy alternative. Falls back to Cost when there is no viable
                // buy alternative (obtainment.BuyCost == 0), so a bought ingredient still reports
                // BuyOrderCost == Cost and yields zero apparent savings.
                ingredient.BuyOrderCost = obtainment.BuyCost > 0 ? obtainment.BuyCost : obtainment.Cost;
                ingredient.CraftCost = obtainment.Method == "craft" ? obtainment.Cost : 0;
                ingredient.Type = obtainment.Method == "buy" ? null : obtainment.Method; // "craft" / "npc" / "bits" / "copper" / "mote" / null(=bought)
            }).ToArray());
            return ingredients.Sum(i => i.Cost);
        }

        /// <summary>
        /// Mirrors <see cref="ItemData.GetIngredients"/>'s recipe-selection rules, but generalizes the
        /// pure-crafting case to yield EVERY crafting recipe (instead of collapsing to one via
        /// <see cref="ItemData.BestRecipe"/>), so the realistic cost engine can compare all of them and
        /// pick the cheapest. Forge/npc_shop/carpentry/malik and legacy single-recipe items still yield
        /// exactly one candidate, unchanged.
        /// </summary>
        internal IEnumerable<(List<Ingredient> ingredients, long yield, string recipeType)> EnumerateRecipeCandidates(ItemData item)
        {
            if (item.recipe != null)
            {
                var ingredients = AggregateSlots(item.recipe.GetIngredients());
                if (ingredients.Count > 0)
                    yield return (ingredients, (long)Math.Max(1, item.recipe.count), "crafting");
                yield break;
            }
            if (item.recipes != null && item.recipes.Count > 0 &&
                (item.recipes[0].type == "forge" || item.recipes[0].type == "npc_shop" || item.recipes[0].type == "carpentry" || item.recipes[0].type == "malik"))
            {
                var ingredients = AggregateSlots(item.recipes[0].inputs);
                if (ingredients.Count > 0)
                    yield return (ingredients, (long)Math.Max(1, item.recipes[0].count), item.recipes[0].type);
                yield break;
            }
            if (item.recipes != null && item.recipes.Count > 0 && item.recipes.All(r => r.type == "crafting"))
            {
                foreach (var recipe in item.recipes)
                {
                    var ingredients = AggregateSlots(recipe.GetIngredients());
                    if (ingredients.Count > 0)
                        yield return (ingredients, (long)Math.Max(1, recipe.count), "crafting");
                }
            }
        }

        /// <summary>
        /// A bit/copper/mote priced ingredient makes the whole craft non-normal (pseudo-currency priced at a
        /// representative rate, or genuinely unobtainable for motes): the parent craft is tagged with that
        /// marker (taking priority over the recipe's own type) so it falls outside CraftsController's
        /// GetProfitable ("crafting"/null) and GetProfitableNpc ("npc"/"npc_shop") filters instead of looking
        /// like a real profitable flip.
        /// </summary>
        internal static string ResolveCraftType(string recipeType, IEnumerable<Ingredient> ingredients)
        {
            var currencyIngredient = ingredients.FirstOrDefault(i => CurrencyIngredientTypes.Contains(i.Type));
            return currencyIngredient?.Type ?? recipeType;
        }

        private static double GetRecipeYield(ItemData item)
        {
            var recipeCount = (item.recipe?.count ?? 0) < 1 ? item.BestRecipe()?.count ?? 1 : item.recipe?.count ?? 1;
            if (recipeCount < 1)
                recipeCount = 1;
            return recipeCount;
        }

        /// <summary>
        /// Builds the price tranches an item can realistically be bought from: npc stock, buy orders
        /// filled by ~30 minutes of insta-sell volume, then the insta-buy order book (or auction bins).
        /// </summary>
        internal async Task<IReadOnlyList<PriceTranche>> GetBuyTranchesAsync(string tag, HashSet<string> bazaarItems)
        {
            var tranches = new List<PriceTranche>();
            var variantTag = ToVariantMarketTag(tag);
            var npc = await GetNpcCosts();
            // NEU internal names encode legacy Minecraft "id:damage" variants (colored wool/dye, logs,
            // stained glass/clay, carpet, ...) with a hyphen for filesystem-safety, e.g. "INK_SACK-4" for
            // lapis lazuli. The actual npc-cost/bazaar market data still keys these by the real Hypixel tag,
            // which uses the legacy colon form ("INK_SACK:4"), so without trying that form too these items
            // never match any npc/bazaar tranche and fall through to the crude AH-sniper/20M-fallback path.
            var npcKey = npc.ContainsKey(tag) ? tag : variantTag;
            if (npc.TryGetValue(npcKey, out var npcUnitPrice) && npcUnitPrice >= 0)
            {
                var stock = npcStock != null && npcStock.TryGetValue(npcKey, out var s) && s > 0 ? s : DefaultNpcStock;
                tranches.Add(new PriceTranche(npcUnitPrice, stock, "npc"));
            }
            var bazaarTag = bazaarItems.Contains(tag) ? tag : (bazaarItems.Contains(variantTag) ? variantTag : null);
            if (orderBookApi != null && bazaarApi != null && bazaarTag != null)
            {
                var batch = await GetBazaarBatchAsync(bazaarItems);
                batch.Prices.TryGetValue(bazaarTag, out var price);
                batch.Books.TryGetValue(bazaarTag, out var book);
                // Buy order channel: a competitive buy order sits at the insta-sell price and only
                // ~30 minutes of insta-sell volume realistically fills it before the price moves on.
                // The unit price includes outbidding the current top order by BuyOrderOutbidCoins (you
                // must sit just above it to get filled) plus the BuyOrderTimeGateMarkup premium for the
                // capital being tied up / at risk while the order fills over time instead of instantly.
                if (price != null && price.SellPrice > 0)
                {
                    var hourlyInstaSell = (long)(price.DailySellVolume / (24 / BuyOrderFillHours));
                    // A single buy order can not hold more than MaxSingleOrderQuantity units regardless of
                    // how much insta-sell volume would otherwise fill it - larger amounts need multiple
                    // sequential orders, which is priced as extra craft-step effort, not a bigger tranche.
                    var orderCapacity = Math.Min(hourlyInstaSell, RealisticCraft.MaxSingleOrderQuantity);
                    if (orderCapacity > 0)
                        tranches.Add(new PriceTranche((price.SellPrice + BuyOrderOutbidCoins) * BuyOrderTimeGateMarkup, orderCapacity, "order"));
                }
                // Everything beyond that is insta-bought, walking the standing sell offers (SmartBuyer
                // sorts cheapest first). The order book depth is a hard cap: you can not buy more than
                // is being offered, so oversized crafts correctly report unmet supply.
                if (book?.Sell != null && book.Sell.Count > 0)
                {
                    foreach (var offer in book.Sell)
                    {
                        var available = offer.Amount - offer.Filled;
                        if (available > 0 && offer.PricePerUnit > 0)
                            tranches.Add(new PriceTranche(offer.PricePerUnit, available, "insta"));
                    }
                }
                else if (price != null && price.BuyPrice > 0 && price.DailyBuyVolume > 0)
                {
                    // No order book depth in this snapshot: fall back to a single bounded insta-buy tranche.
                    tranches.Add(new PriceTranche(price.BuyPrice, price.DailyBuyVolume, "insta"));
                }
            }
            else
            {
                var ah = await GetAhPriceAsync(tag);
                if (ah != null && ah.BuyPrice > 0)
                {
                    var available = ah.Available > 0 ? ah.Available : 1;
                    var unit = ah.BuyPrice / Math.Min(AhSampleCount, available);
                    tranches.Add(new PriceTranche(unit, available, "insta"));
                }
            }
            return tranches;
        }

        /// <summary>Returns the shared bazaar batch for this pass, fetching it once on first use.</summary>
        private Task<BazaarBatch> GetBazaarBatchAsync(HashSet<string> bazaarItems)
        {
            if (bazaarBatchTask != null)
                return bazaarBatchTask;
            lock (bazaarBatchLock)
                bazaarBatchTask ??= FetchBazaarBatchAsync(bazaarItems);
            return bazaarBatchTask;
        }

        private async Task<BazaarBatch> FetchBazaarBatchAsync(HashSet<string> bazaarItems)
        {
            var pricesTask = GetAllBazaarPricesAsync();
            var booksTask = GetAllOrderBooksAsync(bazaarItems);
            await Task.WhenAll(pricesTask, booksTask);
            return new BazaarBatch(await booksTask, await pricesTask);
        }

        private async Task<Dictionary<string, Bazaar.Client.Model.ItemPrice>> GetAllBazaarPricesAsync()
        {
            var map = new Dictionary<string, Bazaar.Client.Model.ItemPrice>();
            try
            {
                var prices = await bazaarApi.GetAllPricesAsync();
                if (prices != null)
                    foreach (var price in prices)
                        if (price?.ProductId != null)
                            map[price.ProductId] = price;
            }
            catch (Exception)
            {
                // Degrade gracefully: without summary prices only npc + order book tranches are used.
            }
            return map;
        }

        private async Task<Dictionary<string, Bazaar.Client.Model.OrderBook>> GetAllOrderBooksAsync(HashSet<string> bazaarItems)
        {
            try
            {
                var books = await orderBookApi.GetOrderBooksAsync(bazaarItems.ToList());
                if (books != null)
                    return new Dictionary<string, Bazaar.Client.Model.OrderBook>(books);
            }
            catch (Exception)
            {
                // Degrade gracefully: without order books, large orders fall back to the summary buy price.
            }
            return new Dictionary<string, Bazaar.Client.Model.OrderBook>();
        }

        private Task<PriceResponse> GetAhPriceAsync(string tag)
        {
            return ahPriceCache.GetOrAdd(tag, async t =>
            {
                try
                {
                    return await GetPriceFor(t, AhSampleCount);
                }
                catch (HttpRequestException)
                {
                    return null;
                }
            });
        }

        private async Task<PriceResponse> GetPriceFor(string itemTag, long count)
        {
            var baseUrl = config["API_BASE_URL"];
            if (baseUrl == "https://sky.coflnet.com")
                await Task.Delay(600); // avoid rate limiting
            var url = $"{config["API_BASE_URL"]}/api/item/price/{System.Web.HttpUtility.UrlEncode(itemTag)}/current?count={count}";
            var response = await client.GetStringAsync(url);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine($"FATAL: Requesting {url} failed with empty response");
                await Task.Delay(1000);
                response = await client.GetStringAsync(url);
            }
            try
            {
                return JsonSerializer.Deserialize<PriceResponse>(response);
            }
            catch (System.Text.Json.JsonException)
            {
                Console.WriteLine($"FATAL: Requesting {url} failed with invalid response '{response}'");
                return new PriceResponse()
                {
                    Available = 0,
                    BuyPrice = 10_000_000_000,
                    SellPrice = 0
                };
            }
        }

        public IEnumerable<Ingredient> NeedCount(ItemData item)
        {
            return AggregateSlots(item.GetIngredients());
        }
        private IEnumerable<Ingredient> GetIngredientsFromSlots(ItemData item)
        {
            return ParseSlots(item.GetIngredients());
        }

        /// <summary>Aggregates one recipe's raw NEU slot strings (e.g. "GOLD_INGOT:5") into tag+count
        /// ingredients (same variant/name parsing as <see cref="ParseSlots"/>, then grouped by tag).</summary>
        private List<Ingredient> AggregateSlots(IEnumerable<string> slots)
        {
            return ParseSlots(slots).GroupBy(i => i.ItemId).Select(i => new Ingredient()
            {
                ItemId = i.Key,
                Count = i.Sum(single => single.Count),
                Type = i.First().Type
            }).ToList();
        }

        /// <summary>Parses raw NEU slot strings ("TAG" or "TAG:count") into individual ingredients,
        /// converting NEU-style names via <see cref="ConvertNeuToTag"/>.</summary>
        private IEnumerable<Ingredient> ParseSlots(IEnumerable<string> slots)
        {
            foreach (var ingredient in slots ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(ingredient))
                    continue;
                var parts = ingredient.Split(':');
                yield return new Ingredient()
                {
                    ItemId = ConvertNeuToTag(parts.First()),
                    Count = parts.Length == 1 ? 1 : (int)float.Parse(parts[1])
                };
            }
        }

        public string ConvertNeuToTag(string neu)
        {
            if (neu.Contains(";"))
                return neu.Replace('-', ':');
            return neu;
        }

        private static readonly System.Text.RegularExpressions.Regex VariantTagPattern =
            new(@"^([A-Za-z0-9_]+)-(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Converts a NEU-style legacy Minecraft "id-damage" variant tag (e.g. "INK_SACK-4", "LOG-1",
        /// "STAINED_GLASS-14", "CARPET-14") into the legacy "id:damage" colon form the real npc-cost and
        /// bazaar market data key these items by. Tags that don't match the trailing-numeric-variant
        /// pattern (the vast majority - normal items never look like this) are returned unchanged, so
        /// this can be applied unconditionally at the market lookup boundary without regressing them.
        /// </summary>
        internal static string ToVariantMarketTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return tag;
            var match = VariantTagPattern.Match(tag);
            return match.Success ? $"{match.Groups[1].Value}:{match.Groups[2].Value}" : tag;
        }

        /// <summary>Market adapter exposing npc + bazaar/auction pricing to the realistic calculator.</summary>
        private class MarketSource : IMarketSource
        {
            private readonly CalculatorService service;
            private readonly HashSet<string> bazaarItems;
            public MarketSource(CalculatorService service, HashSet<string> bazaarItems)
            {
                this.service = service;
                this.bazaarItems = bazaarItems;
            }
            public Task<IReadOnlyList<PriceTranche>> GetBuyTranchesAsync(string tag) => service.GetBuyTranchesAsync(tag, bazaarItems);
        }

        /// <summary>Recipe adapter exposing the craftable item lookup to the realistic calculator.</summary>
        private class RecipeSource : IRecipeSource
        {
            private readonly CalculatorService service;
            private readonly Dictionary<string, ItemData> lookup;
            public RecipeSource(CalculatorService service, Dictionary<string, ItemData> lookup)
            {
                this.service = service;
                this.lookup = lookup;
            }
            public bool TryGetRecipes(string tag, out IReadOnlyList<RecipeOption> recipes)
            {
                recipes = null;
                if (!lookup.TryGetValue(tag, out var data))
                    return false;
                var list = service.EnumerateRecipeCandidates(data)
                    .Select(c => new RecipeOption(c.ingredients.Select(i => (i.ItemId, i.Count)).ToList(), c.yield))
                    .ToList();
                if (list.Count == 0)
                    return false;
                recipes = list;
                return true;
            }
        }
    }
}
