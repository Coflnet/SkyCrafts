using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using BazaarApi = Coflnet.Sky.Bazaar.Client.Api.IBazaarApi;
using BazaarItemPrice = Coflnet.Sky.Bazaar.Client.Model.ItemPrice;
using ItemsItem = Coflnet.Sky.Items.Client.Model.Item;

namespace SkyCrafts.Tests;

public class NpcSellServiceTests
{
    [Fact]
    public async Task GetNpcFlipOpportunities_UsesDailySellVolumeForHourlySells()
    {
        var service = CreateService(out var itemsApi, out var pricesApi, out var bazaarApi);
        itemsApi.ItemsGetAsync().Returns(Task.FromResult<List<ItemsItem>>(
        [
            new ItemsItem { Tag = "ENCHANTED_CLAY_BLOCK", NpcSellPrice = 76800 }
        ]));
        pricesApi.ApiItemPriceItemTagCurrentGetAsync("ENCHANTED_CLAY_BLOCK")
            .Returns(Task.FromResult(new CurrentPrice { Available = 1, Buy = 76450.7 }));
        bazaarApi.GetAllPricesAsync().Returns(Task.FromResult<List<BazaarItemPrice>>(
        [
            new BazaarItemPrice { ProductId = "ENCHANTED_CLAY_BLOCK", DailySellVolume = 268680 }
        ]));

        var flips = await service.GetNpcFlipOpportunities(forceRefresh: true);

        var flip = Assert.Single(flips);
        Assert.Equal("ENCHANTED_CLAY_BLOCK", flip.ItemId);
        Assert.Equal(11195, flip.HourlySells);
    }

    [Fact]
    public async Task GetNpcFlipOpportunities_WhenBazaarRefreshFails_ReusesLastSuccessfulVolumeData()
    {
        var service = CreateService(out var itemsApi, out var pricesApi, out var bazaarApi);
        itemsApi.ItemsGetAsync().Returns(Task.FromResult<List<ItemsItem>>(
        [
            new ItemsItem { Tag = "ENCHANTED_CLAY_BLOCK", NpcSellPrice = 76800 }
        ]));
        pricesApi.ApiItemPriceItemTagCurrentGetAsync("ENCHANTED_CLAY_BLOCK")
            .Returns(Task.FromResult(new CurrentPrice { Available = 1, Buy = 76450.7 }));
        bazaarApi.GetAllPricesAsync().Returns(Task.FromResult<List<BazaarItemPrice>>(
        [
            new BazaarItemPrice { ProductId = "ENCHANTED_CLAY_BLOCK", DailySellVolume = 268680 }
        ]));

        var seededFlip = Assert.Single(await service.GetNpcFlipOpportunities(forceRefresh: true));
        Assert.Equal(11195, seededFlip.HourlySells);

        bazaarApi.GetAllPricesAsync().Returns(Task.FromException<List<BazaarItemPrice>>(new Exception("bazaar unavailable")));

        var flips = await service.GetNpcFlipOpportunities(forceRefresh: true);

        var flip = Assert.Single(flips);
        Assert.Equal(11195, flip.HourlySells);
        Assert.NotEqual(0, flip.HourlySells);
    }

    private static NpcSellService CreateService(out IItemsApi itemsApi, out IPricesApi pricesApi, out BazaarApi bazaarApi)
    {
        itemsApi = Substitute.For<IItemsApi>();
        pricesApi = Substitute.For<IPricesApi>();
        bazaarApi = Substitute.For<BazaarApi>();
        var georgePetOfferService = new GeorgePetOfferService(itemsApi, NullLogger<GeorgePetOfferService>.Instance);

        return new NpcSellService(
            itemsApi,
            pricesApi,
            updaterService: null!,
            georgePetOfferService,
            NullLogger<NpcSellService>.Instance,
            bazaarApi);
    }
}
