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

    [Fact]
    public async Task RawKatDataReturnsScathaBaseCosts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["API_BASE_URL"] = "https://sky.coflnet.com" })
            .Build();
        var service = new KatUpgradeService(configuration, NullLogger<KatUpgradeService>.Instance);
        var controller = new KatController(NullLogger<KatController>.Instance, service);

        var upgrades = await controller.GetUpgradeData();

        // A level 100 pet gets 30% off in game: 87.5m for rare to epic, 175m for epic to legendary.
        Assert.Equal(125_000_000, Assert.Single(upgrades, c => c.Name == "Scatha" && c.BaseRarity == Tier.RARE).Cost);
        Assert.Equal(250_000_000, Assert.Single(upgrades, c => c.Name == "Scatha" && c.BaseRarity == Tier.EPIC).Cost);
    }
}
