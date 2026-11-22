using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Controllers;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.Api.Client.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace SkyCrafts.Tests;

public class KatUpgradeServiceTests
{
    [Fact]
    public void NbtData_DeserializesArbitraryValues()
    {
        var nbt = JsonConvert.DeserializeObject<NbtData>(
            """{"data":{"petInfo":"{\"type\":\"HORSE\"}","exp":123.4}}""");

        Assert.Equal("""{"type":"HORSE"}""", nbt!.Data["petInfo"]);
    }

    [Fact]
    public async Task RawKatDataReturnsEnderDragonBaseCostAndMaterials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["API_BASE_URL"] = "https://sky.coflnet.com" })
            .Build();
        var service = new KatUpgradeService(configuration, NullLogger<KatUpgradeService>.Instance);
        var controller = new KatController(NullLogger<KatController>.Instance, service);

        var upgrade = Assert.Single(await controller.GetUpgradeData(), item => item.ItemTag == "PET_ENDER_DRAGON");
        Assert.Equal(400_000_000, upgrade.Cost);
        Assert.Equal("SUMMONING_EYE", upgrade.Material);
        Assert.Equal(8, upgrade.Amount);
    }
}
