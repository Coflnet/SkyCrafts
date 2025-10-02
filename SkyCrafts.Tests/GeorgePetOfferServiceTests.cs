using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using CoreTier = Coflnet.Sky.Api.Client.Model.Tier;

namespace SkyCrafts.Tests;

public class GeorgePetOfferServiceTests
{
    private static GeorgePetOfferService CreateService(out IItemsApi itemsApi)
    {
        itemsApi = Substitute.For<IItemsApi>();
        return new GeorgePetOfferService(itemsApi, NullLogger<GeorgePetOfferService>.Instance);
    }

    [Fact]
    public async Task GetSnapshotAsync_WithKnownPetTag_ReturnsExpectedPrice()
    {
        var service = CreateService(out var itemsApi);
        var items = new List<Item>
        {
            new Item { Tag = "PET_BLUE_WHALE;4", Tier = null }
        };

        var snapshot = await service.GetSnapshotAsync(items, forceRefresh: true);

        Assert.True(snapshot.OffersByTag.TryGetValue("PET_BLUE_WHALE;4", out var offer));
        Assert.Equal(5_000_000, offer.Price);
        Assert.Equal(CoreTier.LEGENDARY, offer.Key.Rarity);
        Assert.Equal("BLUE_WHALE", offer.Key.BaseTag);
    }

    [Fact]
    public async Task GetSnapshotAsync_NormalizesBaseTagAndSuffix()
    {
        var service = CreateService(out _);
        var items = new List<Item>
        {
            new Item { Tag = "PET_GRANDMA_WOLF_PET;4", Tier = null }
        };

        var snapshot = await service.GetSnapshotAsync(items, forceRefresh: true);

        Assert.True(snapshot.OffersByTag.TryGetValue("PET_GRANDMA_WOLF_PET;4", out var offer));
        Assert.Equal(200_000, offer.Price);
        Assert.Equal(CoreTier.LEGENDARY, offer.Key.Rarity);
        Assert.Equal("GRANDMA_WOLF", offer.Key.BaseTag);
    }

    [Fact]
    public void CreateMockOffers_UsesExpectedNumericSuffix()
    {
        var offers = GeorgePetOfferService.CreateMockOffers();
    var legendaryBlueWhale = offers.First(o => o.Key.BaseTag == "BLUE_WHALE" && o.Key.Rarity == CoreTier.LEGENDARY);

        Assert.Equal("PET_BLUE_WHALE;4", legendaryBlueWhale.ItemTag);
        Assert.Equal(5_000_000, legendaryBlueWhale.Price);
    }
}
