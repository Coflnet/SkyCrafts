using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using IItemsApi = Coflnet.Sky.PlayerState.Client.Api.IItemsApi;

namespace SkyCrafts.Tests;

public class NpcBuyServiceTests
{
    [Theory]
    [InlineData("(1/2) Featured Update Items")]
    [InlineData("(2/2) Featured Update Items")]
    [InlineData("Featured Update Items")]
    [InlineData(" \t(1/2)  fEaTuReD \t Update  Items \n")]
    public async Task GetReverseFlips_ExcludesAlphaShopAndKeepsPagedOphelia(string alphaName)
    {
        var playerItems = Substitute.For<IItemsApi>();
        var items = Substitute.For<IItemApi>();
        var prices = Substitute.For<IPricesApi>();
        playerItems.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>
        {
            Offer("CROWN_OF_AVARICE", alphaName),
            Offer("HYPER_CLEAVER", "(1/2) Ophelia")
        });
        items.ApiItemsGetAsync().Returns(new List<ItemMetadataElement>());
        foreach (var tag in new[] { "CROWN_OF_AVARICE", "HYPER_CLEAVER" })
        {
            prices.ApiItemPriceItemTagGetAsync(tag)
                .Returns(new PriceSumary(median: 1_000_000, volume: 100));
            prices.ApiItemPriceItemTagCurrentGetAsync(tag)
                .Returns(new CurrentPrice { Sell = 1_000_000 });
        }
        var service = new NpcBuyService(playerItems, items, prices, NullLogger<NpcBuyService>.Instance);

        await service.UpdatePrices();

        var flip = Assert.Single(service.GetReverseFlips());
        Assert.Equal("HYPER_CLEAVER", flip.ItemId);
        Assert.Equal(999_999, flip.Profit);
    }

    private static NpcCost Offer(string tag, string npcName) =>
        new(tag, npcName, new Dictionary<string, int> { ["Coins"] = 1 }, resultCount: 1)
        {
            Description = ""
        };
}
