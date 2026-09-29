using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Controllers;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SkyCrafts.Tests;

public class UpdaterServiceTests
{
    [Fact]
    public void ParallelCraftUpdates_PreserveEveryItemWithoutDuplicateIds()
    {
        var updater = new UpdaterService(null!, null!, NullLogger<UpdaterService>.Instance,
            null!, null!, null!, null!, null!, null!, null!);
        var controller = new CraftsController(NullLogger<CraftsController>.Instance, updater, null!, null!);

        Parallel.For(0, 30_000, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i =>
        {
            var tag = "ITEM_" + i;
            updater.Crafts[tag] = new ProfitableCraft { ItemId = tag, CraftCost = i + 1 };
            updater.Crafts["FEATHER_ARTIFACT"] = new ProfitableCraft { ItemId = "FEATHER_ARTIFACT", CraftCost = i + 1 };
        });
        var latest = new ProfitableCraft { ItemId = "FEATHER_ARTIFACT", CraftCost = 50_000 };
        updater.Crafts[latest.ItemId] = latest;

        var crafts = controller.GetAll().ToDictionary(c => c.ItemId);
        Assert.Equal(30_001, crafts.Count);
        Assert.Same(latest, crafts[latest.ItemId]);
        Assert.All(Enumerable.Range(0, 30_000), i => Assert.Equal(i + 1, crafts["ITEM_" + i].CraftCost));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("profit")]
    [InlineData("npc")]
    public void CraftResponse_UsesSnapshotWhileCacheChanges(string endpoint)
    {
        var updater = new UpdaterService(null!, null!, NullLogger<UpdaterService>.Instance,
            null!, null!, null!, null!, null!, null!, null!);
        var controller = new CraftsController(NullLogger<CraftsController>.Instance, updater, null!, null!);
        var original = new ProfitableCraft
        {
            ItemId = "FEATHER_ARTIFACT", CraftCost = 100, SellPrice = 1_000, Volume = 10,
            Ingredients = Array.Empty<Ingredient>(), Type = endpoint == "npc" ? "npc" : "crafting"
        };
        updater.Crafts[original.ItemId] = original;
        var response = endpoint switch
        {
            "profit" => controller.GetProfitable(),
            "npc" => controller.GetProfitableNpc(),
            _ => controller.GetAll()
        };

        updater.Crafts[original.ItemId] = new ProfitableCraft
        {
            ItemId = original.ItemId, CraftCost = 200, SellPrice = 1_000, Volume = 10,
            Ingredients = Array.Empty<Ingredient>(), Type = original.Type
        };
        updater.Crafts["ASPECT_OF_THE_VOID"] = new ProfitableCraft
        {
            ItemId = "ASPECT_OF_THE_VOID", CraftCost = 500, SellPrice = 2_000, Volume = 10,
            Ingredients = Array.Empty<Ingredient>(), Type = original.Type
        };

        Assert.Same(original, Assert.Single(response));
        Assert.Equal(2, controller.GetAll().Count());
    }
}
