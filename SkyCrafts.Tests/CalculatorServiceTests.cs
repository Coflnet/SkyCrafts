using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace SkyCrafts.Tests;

public class CalculatorServiceTests
{
    private sealed class StubPriceCalculator : CalculatorService
    {
        private readonly IReadOnlyDictionary<long, PriceResponse> prices;
        public List<long> RequestedQuantities { get; } = new();

        public StubPriceCalculator(IConfiguration config, IItemsApi playerItemsApi, IReadOnlyDictionary<long, PriceResponse> prices)
            : base(config, playerItemsApi)
        {
            this.prices = prices;
        }

        protected override Task<PriceResponse> GetPriceFor(string itemTag, long count)
        {
            RequestedQuantities.Add(count);
            return Task.FromResult(prices[count]);
        }
    }

    [Fact]
    public async Task GetNpcCosts_ReturnsCorrectPerUnitCost()
    {
        // Arrange
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            new NpcCost("GLASS", "Variety", new Dictionary<string, int> { { "Coins", 4 } }, resultCount: 1),
            new NpcCost("GLASS_BOTTLE", "Alchemist", new Dictionary<string, int> { { "Coins", 48 } }, resultCount: 8),
        });

        var service = new CalculatorService(config, playerItemsApi);

        // Act
        var npcCosts = await service.GetNpcCosts();

        // Assert
        Assert.Equal(4, npcCosts["GLASS"]);
        Assert.Equal(6, npcCosts["GLASS_BOTTLE"]); // 48 / 8 = 6
    }

    [Fact]
    public async Task GetNpcCosts_PicksCheapestSource()
    {
        // Arrange
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            new NpcCost("SAND", "Farm Merchant", new Dictionary<string, int> { { "Coins", 8 } }, resultCount: 1),
            new NpcCost("SAND", "Builder", new Dictionary<string, int> { { "Coins", 2 } }, resultCount: 1),
        });

        var service = new CalculatorService(config, playerItemsApi);

        // Act
        var npcCosts = await service.GetNpcCosts();

        // Assert
        Assert.Equal(2, npcCosts["SAND"]);
    }

    [Fact]
    public async Task GetNpcCosts_IgnoresNonCoinCosts()
    {
        // Arrange
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            new NpcCost("SOME_GEM_ITEM", "NPC", new Dictionary<string, int> { { "Gems", 100 } }, resultCount: 1),
            new NpcCost("MIXED_COST", "NPC", new Dictionary<string, int> { { "Coins", 50 }, { "Copper", 10 } }, resultCount: 1),
        });

        var service = new CalculatorService(config, playerItemsApi);

        // Act
        var npcCosts = await service.GetNpcCosts();

        // Assert
        Assert.False(npcCosts.ContainsKey("SOME_GEM_ITEM"));
        Assert.False(npcCosts.ContainsKey("MIXED_COST"));
    }

    [Fact]
    public async Task GetNpcCosts_AddsHardcodedCheapTuxedoPieceCosts()
    {
        // Arrange
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());

        var service = new CalculatorService(config, playerItemsApi);

        // Act
        var npcCosts = await service.GetNpcCosts();

        // Assert
        Assert.Equal(1_000_000, npcCosts["CHEAP_TUXEDO_BOOTS"]);
        Assert.Equal(1_000_000, npcCosts["CHEAP_TUXEDO_CHESTPLATE"]);
        Assert.Equal(1_000_000, npcCosts["CHEAP_TUXEDO_LEGGINGS"]);
    }

    [Fact]
    public async Task AhTranches_RequestExactQuantity_AndExtendAtMarginalCost()
    {
        var config = Substitute.For<IConfiguration>();
        var playerItemsApi = Substitute.For<IItemsApi>();
        playerItemsApi.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var service = new StubPriceCalculator(config, playerItemsApi, new Dictionary<long, PriceResponse>
        {
            [4] = new() { BuyPrice = 58_459_000, Available = 4, IsAh = true },
            [8] = new() { BuyPrice = 122_448_999, Available = 8, IsAh = true },
        });

        var firstFour = await service.GetBuyTranchesAsync("SHRIVELED_WASP", new HashSet<string>(), 4);
        var nextFour = await service.GetBuyTranchesAsync("SHRIVELED_WASP", new HashSet<string>(), 4, firstFour);

        Assert.Equal(new long[] { 4, 8 }, service.RequestedQuantities);
        Assert.Equal(58_459_000, firstFour.Sum(t => t.UnitPrice * t.Capacity));
        Assert.Equal(122_448_999 - 58_459_000, nextFour.Sum(t => t.UnitPrice * t.Capacity));
        Assert.Equal(4, firstFour.Sum(t => t.Capacity));
        Assert.Equal(4, nextFour.Sum(t => t.Capacity));
    }
}
