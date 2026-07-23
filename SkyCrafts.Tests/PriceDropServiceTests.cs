using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Crafts.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace SkyCrafts.Tests;

public class PriceDropServiceTests
{
    [Fact]
    public async Task UpdateAll_IncludesBazaarItemWithCombinedNumericFlags()
    {
        const string tag = "BOOSTER_COOKIE";
        var pricesApi = Substitute.For<IPricesApi>();
        var itemsApi = Substitute.For<IItemApi>();
        itemsApi.ApiItemsGetAsync().Returns(Task.FromResult<List<ItemMetadataElement>>(
        [
            new ItemMetadataElement(tag, tag, (ItemFlags)17)
        ]));
        pricesApi.ApiItemPriceItemTagHistoryMonthGetAsync(tag, Arg.Any<Dictionary<string, string>>())
            .Returns(Task.FromResult<List<AveragePrice>>(
            [
                new AveragePrice(avg: 12_000_000)
            ]));
        pricesApi.ApiItemPriceItemTagGetAsync(tag, Arg.Any<Dictionary<string, string>>())
            .Returns(Task.FromResult(new PriceSumary(median: 12_500_000, volume: 1000)));
        pricesApi.ApiItemPriceItemTagCurrentGetAsync(tag)
            .Returns(Task.FromResult(new CurrentPrice { Buy = 13_000_000 }));
        var service = new PriceDropService(
            pricesApi,
            NullLogger<PriceDropService>.Instance,
            itemsApi);

        await service.UpdateAll(new Dictionary<string, Coflnet.Sky.Crafts.Models.ProfitableCraft>());

        var statistic = Assert.Single(service.GetAllDrops());
        Assert.Equal(tag, statistic.Tag);
        Assert.Equal(12_000_000, statistic.Monthly);
        Assert.Equal(12_500_000, statistic.Recent);
        Assert.Equal(13_000_000, statistic.Now);
        await itemsApi.DidNotReceive().ApiItemsBazaarTagsGetAsync();
    }
}
