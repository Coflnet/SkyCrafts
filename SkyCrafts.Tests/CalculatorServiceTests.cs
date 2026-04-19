using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace SkyCrafts.Tests;

public class CalculatorServiceTests
{
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
}
