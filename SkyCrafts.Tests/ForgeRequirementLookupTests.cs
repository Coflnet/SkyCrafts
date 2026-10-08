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
}
