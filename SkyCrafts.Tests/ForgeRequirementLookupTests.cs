using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Xunit;

namespace SkyCrafts.Tests;

public class ForgeRequirementLookupTests
{
    // Names/tiers verified against the official wiki's Forge table. Resolved through the real file.
    [Theory]
    // Present in the file but the apostrophe used to be stripped from the item name only, so no key matched.
    [InlineData("§dDivan's Drill", 7)]
    [InlineData("§dDivan's Powder Coating", 4)]
    // Genuinely missing from the file.
    [InlineData("§aMithril Lantern", 2)]
    [InlineData("§9Titanium Lantern", 5)]
    [InlineData("§5Glacite Lantern", 8)]
    [InlineData("§6Will-o'-wisp", 10)]
    // Wrong values that were corrected.
    [InlineData("§aMithril Gauntlet", 2)]
    [InlineData("§aMithril Belt", 2)]
    [InlineData("§aMithril Cloak", 2)]
    [InlineData("§aMithril Necklace", 2)]
    [InlineData("§6Golden Plate", 3)]
    public void HotmTier_ResolvesThroughTheRealFile(string displayName, int tier)
    {
        Assert.Equal(tier, ForgeRequirementLookup.GetHotmTier(new ItemData { displayname = displayName }));
    }

    [Fact]
    public void HotmTier_FallsBackToCrafttext_WhenTheFileHasNoEntry()
    {
        var item = new ItemData { displayname = "Not In The File", crafttext = "Requires: HotM 5" };

        Assert.Equal(5, ForgeRequirementLookup.GetHotmTier(item));
        Assert.Null(ForgeRequirementLookup.GetHotmTier(new ItemData { displayname = "Not In The File" }));
    }

    [Fact]
    public void Clean_KeepsDigitsIncludingZero_AndIgnoresApostrophesOnBothSides()
    {
        Assert.Equal("Drill 10", ForgeRequirementLookup.Clean("§aDrill 10"));
        Assert.Equal("Divans Drill", ForgeRequirementLookup.Clean("§dDivan's Drill"));
    }

    // Regression: collection levels are roman numerals and some requirements have no level at all; the forge
    // flip service parses every level as int and used to throw (dropping the flip) for such items.
    [Theory]
    [InlineData("Heart of the Mountain Tier 7", "Heart of the Mountain Tier", "7")]
    [InlineData("Umber Collection III", "Umber Collection", "3")]
    [InlineData("Tungsten Collection IV", "Tungsten Collection", "4")]
    [InlineData("Gemstone Collection X", "Gemstone Collection", "10")]
    [InlineData("Talk to Dulin", "Talk to Dulin", "1")]
    [InlineData("Donating a Tusk Fossil to Dr. Stone", "Donating a Tusk Fossil to Dr. Stone", "1")]
    public void SplitLevel_AlwaysYieldsANumericLevel(string requirement, string name, string level)
    {
        Assert.Equal((name, level), ForgeRequirementLookup.SplitLevel(requirement));
    }

    [Fact]
    public void EveryRequirementInTheRealFile_HasANumericLevel()
    {
        var umber = ForgeRequirementLookup.GetRequirements(new ItemData { displayname = "§9Refined Umber" });
        Assert.Equal("3", umber["Umber Collection"]);
        Assert.Equal("7", umber["HotM"]);
        var raw = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string[]>>(
            System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data/forge_requirements.json")));
        foreach (var name in raw.Keys)
            foreach (var requirement in ForgeRequirementLookup.GetRequirements(new ItemData { displayname = name }))
                Assert.True(int.TryParse(requirement.Value, out _), $"{name}: {requirement.Key} = {requirement.Value}");
    }
}
