using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace SkyCrafts.Tests;

/// <summary>
/// Covers the batch bazaar pricing path: the whole order book + summary prices are pulled in two
/// batch calls per pass and turned into supply-limited tranches. Guards the spread semantics
/// (insta-sell price feeds the buy-order channel, standing sell offers feed the insta-buy walk) and
/// the batching itself (one call each, reused across items until the caches are reset).
/// </summary>
public class BazaarBatchPricingTests
{
    private const string Obsidian = "OBSIDIAN";

    private static OrderEntry Entry(int amount, double pricePerUnit, bool isSell, int filled = 0) =>
        new OrderEntry(amount, pricePerUnit, "player", "uuid", isSell, DateTime.MinValue, Obsidian, false, filled, false);

    private static (CalculatorService service, IBazaarApi bazaar, IOrderBookApi orderBook) BuildService()
    {
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            // npc obsidian: 30 coins each. Stock is left at 0 so the default per-restock cap applies.
            new NpcCost(Obsidian, "Builder", new Dictionary<string, int> { { "Coins", 30 } }, resultCount: 1),
        });

        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>
        {
            // sellPrice 50 = insta-sell price (a competitive buy order sits here);
            // dailySellVolume 240000 => ~30 min of insta-sells = 5000 fill the buy order channel.
            new ItemPrice(Obsidian, buyPrice: 70, dailyBuyVolume: 500, dailySellVolume: 240000, sellPrice: 50),
        });

        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>
        {
            [Obsidian] = new OrderBook(
                buy: new List<OrderEntry> { Entry(1000, 48, isSell: false) },   // buy orders: must NOT feed the insta-buy walk
                sell: new List<OrderEntry>                                       // sell offers: the insta-buy walk, cheapest first
                {
                    Entry(5000, 70, isSell: true),
                    Entry(100000, 90, isSell: true),
                }),
        });

        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);
        return (service, bazaar, orderBook);
    }

    [Fact]
    public async Task BuildsNpcOrderAndInstaTranches_FromBatchData()
    {
        var (service, _, _) = BuildService();

        var tranches = await service.GetBuyTranchesAsync(Obsidian, new HashSet<string> { Obsidian }, 110_640);

        // npc: cheapest, capped at the default per-restock stock.
        var npc = Assert.Single(tranches.Where(t => t.Source == "npc"));
        Assert.Equal(30, npc.UnitPrice);
        Assert.Equal(CalculatorService.DefaultNpcStock, npc.Capacity);

        // buy order channel: priced at the insta-sell price (50) outbid by BuyOrderOutbidCoins and
        // marked up by BuyOrderTimeGateMarkup, NOT the raw insta-sell price (50) or insta-buy price
        // (70), and capped at ~30 min of insta-sell volume (240000 / 48 = 5000).
        var order = Assert.Single(tranches.Where(t => t.Source == "order"));
        Assert.Equal((50 + 0.2) * 1.20, order.UnitPrice, 6);
        Assert.Equal(5000, order.Capacity);

        // insta-buy walk: comes from the standing sell offers, never the buy orders (48).
        var insta = tranches.Where(t => t.Source == "insta").OrderBy(t => t.UnitPrice).ToList();
        Assert.Equal(new double[] { 70, 90 }, insta.Select(t => t.UnitPrice).ToArray());
        Assert.Equal(new long[] { 5000, 100000 }, insta.Select(t => t.Capacity).ToArray());
        Assert.DoesNotContain(tranches, t => t.UnitPrice == 48);
    }

    [Fact]
    public async Task SellOfferDepthIsReducedByAlreadyFilledAmount()
    {
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>());
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>
        {
            [Obsidian] = new OrderBook(
                buy: new List<OrderEntry>(),
                sell: new List<OrderEntry> { Entry(1000, 90, isSell: true, filled: 600) }),
        });
        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        var tranches = await service.GetBuyTranchesAsync(Obsidian, new HashSet<string> { Obsidian }, 400);

        var insta = Assert.Single(tranches.Where(t => t.Source == "insta"));
        Assert.Equal(400, insta.Capacity); // 1000 offered - 600 already filled
    }

    [Fact]
    public async Task BatchIsFetchedOnce_AndSharedAcrossItems()
    {
        var (service, bazaar, orderBook) = BuildService();
        var bazaarItems = new HashSet<string> { Obsidian, "ENCHANTED_OBSIDIAN" };

        await service.GetBuyTranchesAsync(Obsidian, bazaarItems, 1);
        await service.GetBuyTranchesAsync("ENCHANTED_OBSIDIAN", bazaarItems, 1);
        await service.GetBuyTranchesAsync(Obsidian, bazaarItems, 1);

        // Despite three lookups across two items, each batch endpoint is hit exactly once.
        await bazaar.Received(1).GetAllPricesAsync();
        await orderBook.Received(1).GetOrderBooksAsync(Arg.Any<List<string>>());
    }

    [Fact]
    public async Task OrderTrancheCapacity_IsCappedAtSingleOrderMax_EvenWithEnormousDailyVolume()
    {
        // An enormous daily sell volume would otherwise imply a huge "order" tranche, but a single
        // bazaar buy order can never hold more than RealisticCraft.MaxSingleOrderQuantity units, so
        // the tranche capacity must be capped there regardless of how much insta-sell volume exists.
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var bazaar = Substitute.For<IBazaarApi>();
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>
        {
            new ItemPrice(Obsidian, buyPrice: 70, dailyBuyVolume: 500, dailySellVolume: 100_000_000, sellPrice: 50),
        });
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>
        {
            [Obsidian] = new OrderBook(buy: new List<OrderEntry>(), sell: new List<OrderEntry>()),
        });
        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        var tranches = await service.GetBuyTranchesAsync(Obsidian, new HashSet<string> { Obsidian }, RealisticCraft.MaxSingleOrderQuantity);

        var order = Assert.Single(tranches.Where(t => t.Source == "order"));
        Assert.Equal(RealisticCraft.MaxSingleOrderQuantity, order.Capacity);
    }

    [Fact]
    public async Task ResetPriceCaches_RefetchesTheBatch()
    {
        var (service, bazaar, orderBook) = BuildService();
        var bazaarItems = new HashSet<string> { Obsidian };

        await service.GetBuyTranchesAsync(Obsidian, bazaarItems, 1);
        service.ResetPriceCaches();
        await service.GetBuyTranchesAsync(Obsidian, bazaarItems, 1);

        await bazaar.Received(2).GetAllPricesAsync();
        await orderBook.Received(2).GetOrderBooksAsync(Arg.Any<List<string>>());
    }

    [Fact]
    public async Task OrderTrancheUnitPrice_OutbidsTopOrderAndAppliesTimeGateMarkup()
    {
        // Regression test: the "order" tranche unit price must be the realistic cost of actually
        // getting a competitive buy order filled - outbidding the current top order by
        // BuyOrderOutbidCoins (0.2), then a BuyOrderTimeGateMarkup (20%) premium for the capital
        // being tied up while the order fills over time instead of instantly. It must NOT be the
        // raw sellPrice.
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var bazaar = Substitute.For<IBazaarApi>();
        const double sellPrice = 1000;
        bazaar.GetAllPricesAsync().Returns(new List<ItemPrice>
        {
            // Enough daily sell volume to yield a positive order capacity.
            new ItemPrice(Obsidian, buyPrice: 1100, dailyBuyVolume: 500, dailySellVolume: 240000, sellPrice: sellPrice),
        });
        var orderBook = Substitute.For<IOrderBookApi>();
        orderBook.GetOrderBooksAsync(Arg.Any<List<string>>()).Returns(new Dictionary<string, OrderBook>
        {
            [Obsidian] = new OrderBook(buy: new List<OrderEntry>(), sell: new List<OrderEntry>()),
        });
        var service = new CalculatorService(config, playerItemsApi, bazaar, orderBook);

        var tranches = await service.GetBuyTranchesAsync(Obsidian, new HashSet<string> { Obsidian }, 501);

        var order = Assert.Single(tranches.Where(t => t.Source == "order"));
        Assert.True(order.Capacity > 0);
        Assert.Equal((sellPrice + 0.2) * 1.20, order.UnitPrice, 6);
    }
}
