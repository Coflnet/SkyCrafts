using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace SkyCrafts.Tests;

/// <summary>
/// Fix #1: live coinsPerBit/coinsPerCopper rate sourcing (reusing the shared bazaar batch, no second
/// bazaar fetch, graceful fallback on failure) and the craft Type marker propagation. Fix #2: legacy
/// "-N" variant tags (INK_SACK-4, LOG-1, ...) resolving to their real npc/bazaar price instead of the
/// generic fallback. All market/recipe/rate sources are mocked - no live services are contacted.
/// </summary>
public class CurrencyAndVariantPricingTests
{
    private static (CalculatorService service, IConfiguration config, IItemsApi playerItemsApi) BuildBase()
    {
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        return (null, config, playerItemsApi);
    }

    [Fact]
    public async Task GetCoinsPerBitAsync_DividesBoosterCookiePrice_BySecondStageBitsPerCookie()
    {
        var (_, config, playerItemsApi) = BuildBase();
        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>
        {
            new ItemPrice("BOOSTER_COOKIE", buyPrice: 10_560_000, dailyBuyVolume: 0, dailySellVolume: 0, sellPrice: 0),
        });
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>());

        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        var rate = await service.GetCoinsPerBitAsync(new HashSet<string>());

        Assert.Equal(2000, rate, 6); // 10_560_000 / 5280 = 2000
        // The bit-rate lookup must not trigger its own bazaar fetch: only the one shared batch call.
        await bazaar.Received(1).GetAllPricesAsync();
    }

    [Fact]
    public async Task GetCoinsPerBitAsync_FallsBackToDefault_WhenBoosterCookiePriceMissing()
    {
        var (_, config, playerItemsApi) = BuildBase();
        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>());
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>());

        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook); // no booster cookie price

        var rate = await service.GetCoinsPerBitAsync(new HashSet<string>());

        Assert.Equal(2000, rate); // documented default fallback, never throws
    }

    [Fact]
    public async Task GetCoinsPerCopperAsync_UsesCheapestAcquisitionCost_ViaMaxCopperPerCoin()
    {
        var (_, config, playerItemsApi) = BuildBase();
        var flipperApi = Substitute.For<IBazaarFlipperApi>();
        flipperApi.CopperGetAsync().Returns(new List<CopperFlip>
        {
            // (buyPrice+analyzeCost)/yield = (10000+10000)/5 = 4000 coins/copper -> copperPerCoin = 1/4000
            new CopperFlip("ASHWREATH", buyPrice: 10000, analyzeCost: 10000, copperYield: 5, totalCost: 20000, copperPerCoin: 5d / 20000),
            // (60000+0)/30 = 2000 coins/copper (cheapest -> highest copperPerCoin) -> chosen
            new CopperFlip("CHOCOBERRY", buyPrice: 60000, analyzeCost: 0, copperYield: 30, totalCost: 60000, copperPerCoin: 30d / 60000),
        });

        var service = new CalculatorService(config, playerItemsApi, bazaarFlipperApi: flipperApi);

        var rate = await service.GetCoinsPerCopperAsync();

        Assert.Equal(2000, rate, 6);
    }

    [Fact]
    public async Task GetCoinsPerCopperAsync_FallsBackToDefault_WhenFlipperApiMissing()
    {
        var (_, config, playerItemsApi) = BuildBase();
        var service = new CalculatorService(config, playerItemsApi); // no bazaarFlipperApi

        var rate = await service.GetCoinsPerCopperAsync();

        Assert.Equal(2000, rate); // documented default fallback, matching the AnalyzeCost/CopperYield floor
    }

    [Fact]
    public void ResolveCraftType_CurrencyIngredient_OverridesRecipeType()
    {
        var ingredients = new List<Ingredient>
        {
            new() { ItemId = "SOME_ITEM", Type = null },
            new() { ItemId = "SKYBLOCK_BIT", Type = "bits" },
        };

        var resolved = CalculatorService.ResolveCraftType("crafting", ingredients);

        Assert.Equal("bits", resolved);
        // Sanity: this must fall outside CraftsController's exact allow-lists.
        Assert.NotEqual("crafting", resolved);
        Assert.False(resolved == null || resolved == "crafting");
        Assert.False(resolved == "npc" || resolved == "npc_shop");
    }

    [Fact]
    public void ResolveCraftType_NoCurrencyIngredient_KeepsRecipeType()
    {
        var ingredients = new List<Ingredient> { new() { ItemId = "SOME_ITEM", Type = null } };

        var resolved = CalculatorService.ResolveCraftType("crafting", ingredients);

        Assert.Equal("crafting", resolved);
    }

    [Theory]
    [InlineData("forge")]
    [InlineData("npc_shop")]
    public void ResolveCraftType_IndirectRecipeType_StaysTagged_ExcludingItFromGetProfitable(string recipeType)
    {
        // Fix #3 (corrected): forge/npc_shop steps no longer get a cost penalty - the coin cost is now
        // the real, uninflated cost. What keeps them out of CraftsController.GetProfitable's "craft
        // flip" results (predicate: c.Type == null || c.Type == "crafting") is purely this Type marker,
        // which ResolveCraftType must not clear away when there is no currency ingredient involved.
        var ingredients = new List<Ingredient> { new() { ItemId = "RAW", Type = null } };

        var resolved = CalculatorService.ResolveCraftType(recipeType, ingredients);

        Assert.Equal(recipeType, resolved);
        Assert.False(resolved == null || resolved == "crafting"); // excluded from GetProfitable
    }

    // --- Fix #2: legacy "-N" variant tags resolve to their real cheap price ---

    [Fact]
    public async Task VariantTag_ResolvesToBazaarPrice_ViaColonForm()
    {
        var (_, config, playerItemsApi) = BuildBase();
        var bazaar = Substitute.For<IBazaarApi>();
        // The real Hypixel/bazaar product id for this legacy variant is the colon form.
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>
        {
            new ItemPrice("INK_SACK:4", buyPrice: 250, dailyBuyVolume: 10_000, dailySellVolume: 0, sellPrice: 0),
        });
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>());
        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        // Queried with the NEU-style hyphen internalname, as ingredient parsing produces.
        var tranches = await service.GetBuyTranchesAsync("INK_SACK-4", new HashSet<string> { "INK_SACK:4" });

        var insta = Assert.Single(tranches);
        Assert.Equal(250, insta.UnitPrice);
        Assert.Equal("insta", insta.Source);
    }

    [Fact]
    public async Task VariantTag_ResolvesToNpcPrice_ViaColonForm()
    {
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            new NpcCost("LOG:1", "Lumberjack", new Dictionary<string, int> { { "Coins", 25 } }, resultCount: 1),
        });
        // bazaar+order book substitutes keep the tag on the bazaar-batch path instead of the AH-sniper
        // fallback (which would otherwise make a real HTTP call); the tag just isn't bazaar-sold, so
        // they resolve to no tranches of their own, leaving only the npc tranche.
        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>());
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>());
        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        var tranches = await service.GetBuyTranchesAsync("LOG-1", new HashSet<string> { "LOG:1" });

        var npc = Assert.Single(tranches);
        Assert.Equal(25, npc.UnitPrice);
        Assert.Equal("npc", npc.Source);
    }

    [Fact]
    public async Task NonVariantTag_IsNotRegressed_ByVariantNormalization()
    {
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            new NpcCost("OBSIDIAN", "Builder", new Dictionary<string, int> { { "Coins", 30 } }, resultCount: 1),
        });
        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>());
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>());
        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        var tranches = await service.GetBuyTranchesAsync("OBSIDIAN", new HashSet<string> { "OBSIDIAN" });

        var npc = Assert.Single(tranches);
        Assert.Equal(30, npc.UnitPrice);
    }
}
