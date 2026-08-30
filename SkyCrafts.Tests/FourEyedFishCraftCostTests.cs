using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace SkyCrafts.Tests;

public class FourEyedFishCraftCostTests
{
    private sealed class FixedPriceCalculator : CalculatorService
    {
        private readonly IReadOnlyDictionary<string, PriceResponse> prices;

        public FixedPriceCalculator(IConfiguration config, IItemsApi itemsApi, IBazaarApi bazaarApi,
            IOrderBookApi orderBookApi, IReadOnlyDictionary<string, PriceResponse> prices)
            : base(config, itemsApi, bazaarApi, orderBookApi)
        {
            this.prices = prices;
        }

        protected override Task<PriceResponse> GetPriceFor(string itemTag, long count) =>
            Task.FromResult(prices[itemTag]);
    }

    [Fact]
    public async Task FourEyedFish_DoesNotApplyUnmetSupplyPenaltyWhenPreDigestionFishHasNoAhSupply()
    {
        var service = BuildService();
        var fish = BuildFourEyedFish();

        var result = await service.GetCreaftingCost(fish, new Dictionary<string, ProfitableCraft>(),
            new Dictionary<string, ItemData> { [fish.internalname] = fish },
            new HashSet<string> { "ENCHANTED_SPIDER_EYE" });

        Assert.Equal(6_957_392, result.CraftCost);
        Assert.Equal(485_257_932, result.SellPrice);
        Assert.Equal("crafting", result.Type);

        var ingredients = result.Ingredients.ToDictionary(i => i.ItemId);
        Assert.Equal(4, ingredients.Count);
        Assert.Equal(384, ingredients["ENCHANTED_SPIDER_EYE"].Count);
        Assert.Equal(307_392, ingredients["ENCHANTED_SPIDER_EYE"].Cost);
        Assert.Equal(384, ingredients["ENCHANTED_SPIDER_EYE"].InstaBuyCapacity);
        Assert.Equal(800.5, ingredients["ENCHANTED_SPIDER_EYE"].InstaBuyUnitPrice);
        Assert.Equal(1_650_000, ingredients["PET_ITEM_LUCKY_CLOVER"].Cost);
        Assert.Equal(5_000_000, ingredients["REINFORCED_SCALES"].Cost);
        Assert.Equal(1, ingredients["PRE_DIGESTION_FISH"].Count);
        Assert.Equal(0, ingredients["PRE_DIGESTION_FISH"].Cost);
        Assert.Null(ingredients["PRE_DIGESTION_FISH"].Type);
        Assert.Equal(0, ingredients["PRE_DIGESTION_FISH"].NpcCapacity);
        Assert.Equal(0, ingredients["PRE_DIGESTION_FISH"].BuyOrderCapacity);
        Assert.Equal(0, ingredients["PRE_DIGESTION_FISH"].InstaBuyCapacity);
    }

    [Fact]
    public async Task FourEyedFish_UsesAvailableAhPriceForPreDigestionFish()
    {
        var service = BuildService(new PriceResponse { BuyPrice = 12_345, Available = 1, IsAh = true });
        var fish = BuildFourEyedFish();

        var result = await service.GetCreaftingCost(fish, new Dictionary<string, ProfitableCraft>(),
            new Dictionary<string, ItemData> { [fish.internalname] = fish },
            new HashSet<string> { "ENCHANTED_SPIDER_EYE" });

        Assert.Equal(6_969_737, result.CraftCost);
        var ingredient = Assert.Single(result.Ingredients, i => i.ItemId == "PRE_DIGESTION_FISH");
        Assert.Equal(12_345, ingredient.Cost);
        Assert.Equal(1, ingredient.InstaBuyCapacity);
        Assert.Equal(12_345, ingredient.InstaBuyUnitPrice);
    }

    private static CalculatorService BuildService(PriceResponse preDigestionFishPrice = null)
    {
        var config = Substitute.For<IConfiguration>();
        var itemsApi = Substitute.For<IItemsApi>();
        itemsApi.ApiItemsNpccostGetAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new List<NpcCost>());
        var bazaarApi = Substitute.For<IBazaarApi>();
        bazaarApi.GetAllPricesAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(new List<ItemPrice>
        {
            new("ENCHANTED_SPIDER_EYE", buyPrice: 800.5, dailyBuyVolume: 10_000,
                dailySellVolume: 0, sellPrice: 0),
        });
        var orderBookApi = Substitute.For<IOrderBookApi>();
        orderBookApi.GetOrderBooksAsync(Arg.Any<List<string>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, OrderBook>());
        var prices = new Dictionary<string, PriceResponse>
        {
            ["FOUR_EYED_FISH"] = new() { SellPrice = 485_257_932, Available = 1, IsAh = true },
            ["PET_ITEM_LUCKY_CLOVER"] = new() { BuyPrice = 1_650_000, Available = 1, IsAh = true },
            ["PRE_DIGESTION_FISH"] = preDigestionFishPrice ?? new() { BuyPrice = 0, Available = 0, IsAh = true },
            ["REINFORCED_SCALES"] = new() { BuyPrice = 5_000_000, Available = 1, IsAh = true },
        };
        return new FixedPriceCalculator(config, itemsApi, bazaarApi, orderBookApi, prices);
    }

    private static ItemData BuildFourEyedFish()
    {
        return new ItemData
        {
            internalname = "FOUR_EYED_FISH",
            displayname = "Four-eyed Fish",
            recipe = new Coflnet.Sky.Crafts.Models.Recipe
            {
                A1 = "ENCHANTED_SPIDER_EYE:64",
                A2 = "ENCHANTED_SPIDER_EYE:64",
                A3 = "ENCHANTED_SPIDER_EYE:64",
                B1 = "PET_ITEM_LUCKY_CLOVER:1",
                B2 = "PRE_DIGESTION_FISH:1",
                B3 = "REINFORCED_SCALES:1",
                C1 = "ENCHANTED_SPIDER_EYE:64",
                C2 = "ENCHANTED_SPIDER_EYE:64",
                C3 = "ENCHANTED_SPIDER_EYE:64",
                count = 1,
            },
        };
    }
}
