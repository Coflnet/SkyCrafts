using System.Collections.Generic;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Xunit;

namespace SkyCrafts.Tests;

public class CraftingRecipeServiceTests
{
    [Theory]
    [InlineData("CRIMSON_HELMET", 10, 3935000)]
    [InlineData("HOT_AURORA_CHESTPLATE", 20, 6935000)]
    [InlineData("BURNING_CRIMSON_LEGGINGS", 50, 11935000)]
    [InlineData("FIERY_AURORA_BOOTS", 80, 21935000)]
    [InlineData("TERROR_HELMET", 10, 1935000)]
    [InlineData("HOT_FERVOR_CHESTPLATE", 20, 1935000)]
    [InlineData("BURNING_HOLLOW_LEGGINGS", 50, 1935000)]
    public void CalculatePrestigeCoinCost_ReturnsCorrectCost(string itemId, int teeth, long expectedCost)
    {
        var ingredients = new List<Ingredient>
        {
            new Ingredient { ItemId = "KUUDRA_TEETH", Count = teeth }
        };

        var actualCost = CraftingRecipeService.CalculatePrestigeCoinCost(itemId, ingredients);

        Assert.Equal(expectedCost, actualCost);
    }
}