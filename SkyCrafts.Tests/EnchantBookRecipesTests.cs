using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;
using NpcCost = Coflnet.Sky.PlayerState.Client.Model.NpcCost;

namespace SkyCrafts.Tests;

public class EnchantBookRecipesTests
{
    private static ItemData Book(string neuName, string title, params string[] source)
    {
        var lore = new List<string> { "§8Combinable in Anvil", "", title, "§7Some description", "" };
        if (source.Length > 0)
        {
            lore.Add("§6Source:");
            lore.AddRange(source);
            lore.Add("");
        }
        lore.Add("§6Applied To:");
        return new ItemData { itemid = "minecraft:enchanted_book", displayname = "§fEnchanted Book", internalname = neuName, lore = lore };
    }

    private static List<ItemData> Levels(string name, int max, params string[] source)
        => Enumerable.Range(1, max).Select(l => Book($"{name};{l}", $"§9{name} {l}", source)).ToList();

    private static int[] AnvilLevels(List<ItemData> books)
    {
        var craftable = EnchantBookRecipes.PrepareCraftableBooks(books);
        return craftable.Where(b => b.recipes?.Any(r => r.type == "anvil") == true)
            .Select(b => { EnchantBookRecipes.TryParseTag(b.internalname, out _, out var l); return l; }).Order().ToArray();
    }

    [Theory]
    [InlineData("I", 1)]
    [InlineData("IV", 4)]
    [InlineData("V", 5)]
    [InlineData("VII", 7)]
    [InlineData("IX", 9)]
    [InlineData("X", 10)]
    [InlineData("XIV", 14)]
    [InlineData("", 0)]
    [InlineData("ABC", 0)]
    public void ParseRoman_Works(string roman, int expected) => Assert.Equal(expected, EnchantBookRecipes.ParseRoman(roman));

    [Fact]
    public void ParseSourceRanges_HandlesRangesSinglesAndContinuationLines()
    {
        var lore = new[]
        {
            "§8Combinable in Anvil", "", "§9Luck I", "§7desc", "",
            "§6Source:", "§aI-V: §7Enchantment Table,", "§7Raw Rabbit Collection,", "§aVI: §7Season of Jerry,", "§aIII-V: §7Dark Auction.", "",
            "§6Applied To:", "§aI-X: §7not a source"
        };
        Assert.Equal([(1, 5), (6, 6), (3, 5)], EnchantBookRecipes.ParseSourceRanges(lore));
    }

    [Fact]
    public void ParseSourceRanges_NoSourceSection_IsEmpty()
        => Assert.Empty(EnchantBookRecipes.ParseSourceRanges(["§8Combinable in Anvil", "", "§9Scuba I"]));

    [Fact]
    public void LuckLike_IsAnvilCraftableFrom2To5()
    {
        var books = Levels("LUCK", 7, "§aI-V: §7Enchantment Table,", "§7Raw Rabbit Collection,", "§aVI: §7Season of Jerry,");
        Assert.Equal(new[] { 2, 3, 4, 5 }, AnvilLevels(books));
    }

    [Fact]
    public void Chance_OnlyTwoToThree() => Assert.Equal(new[] { 2, 3 }, AnvilLevels(Levels("CHANCE", 5, "§aI-III: §7Enchantment Table,", "§aIV: §7Experiments", "§aV: §7Experiments")));

    [Fact]
    public void FireAspect_OnlyLevelTwo() => Assert.Equal(new[] { 2 }, AnvilLevels(Levels("FIRE_ASPECT", 3, "§aI-II: §7Enchantment Table,", "§aIII: §7Something")));

    [Fact]
    public void FerociousMana_TwoToTen() => Assert.Equal(Enumerable.Range(2, 9).ToArray(), AnvilLevels(Levels("FEROCIOUS_MANA", 10, "§aI-X: §7Kuudra")));

    [Fact]
    public void BigBrain_FourToFive() => Assert.Equal(new[] { 4, 5 }, AnvilLevels(Levels("BIG_BRAIN", 5, "§aIII-V: §7Dark Auction.")));

    [Fact]
    public void NoSourceSection_NoAnvil() => Assert.Empty(AnvilLevels(Levels("SCUBA", 3)));

    [Fact]
    public void SourceRanges_AreUnionedOverAllLevelFiles()
    {
        var books = Levels("DEDICATION", 4);
        books[0].lore.InsertRange(5, ["§6Source:", "§aI-IV: §7Catacombs", ""]);
        Assert.Equal(new[] { 2, 3, 4 }, AnvilLevels(books));
    }

    [Fact]
    public void MissingLowerLevelBook_NoAnvilRecipe()
    {
        var books = Levels("X", 5, "§aI-V: §7Table").Where(b => !b.internalname.EndsWith(";2")).ToList();
        Assert.Equal(new[] { 4, 5 }, AnvilLevels(books)); // 3 has no level 2 base
    }

    [Fact]
    public void Normalize_RenamesTagAndUsesRealDisplayName()
    {
        var book = Book("LUCK;4", "§9Luck IV");
        EnchantBookRecipes.Normalize(book);
        Assert.Equal("ENCHANTMENT_LUCK_4", book.internalname);
        Assert.Equal("§9Luck IV", book.displayname);

        var ult = Book("ULTIMATE_WISE;1", "§d§lUltimate Wise I");
        EnchantBookRecipes.Normalize(ult);
        Assert.Equal("ENCHANTMENT_ULTIMATE_WISE_1", ult.internalname);
        Assert.Equal("§d§lUltimate Wise I", ult.displayname);
    }

    [Fact]
    public void AnvilRecipe_HasExpectedShape()
    {
        var books = Levels("LUCK", 5, "§aI-V: §7Table");
        var luck5 = EnchantBookRecipes.PrepareCraftableBooks(books).Single(b => b.internalname == "ENCHANTMENT_LUCK_5");
        var recipe = Assert.Single(luck5.recipes);
        Assert.Equal("anvil", recipe.type);
        Assert.Equal(["ENCHANTMENT_LUCK_4:2"], recipe.inputs);
        Assert.Equal(1, recipe.count);
    }

    [Theory]
    [InlineData("LUCK", 5, new[] { 2, 3, 4, 5 })]
    [InlineData("ULTIMATE_WISE", 5, new[] { 2, 3, 4, 5 })]
    [InlineData("FIRE_ASPECT", 3, new[] { 2 })]
    public void RealItemData_MatchesExpectedLevels(string name, int maxLevel, int[] expected)
    {
        var dir = FindItemDir();
        if (dir == null)
            return; // item repo not cloned
        var books = Enumerable.Range(1, maxLevel).Select(l => Path.Combine(dir, $"{name};{l}.json")).Where(File.Exists)
            .Select(p => JsonSerializer.Deserialize<ItemData>(File.ReadAllText(p))).ToList();
        Assert.Equal(expected, AnvilLevels(books));
    }

    private static string FindItemDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "itemData", "items")))
            dir = Path.GetDirectoryName(dir);
        return dir == null ? null : Path.Combine(dir, "itemData", "items");
    }

    private static ItemData LuckLikeBook(int level)
    {
        var book = Book($"LUCK;{level}", $"§9Luck {level}", "§aI-V: §7Enchantment Table");
        if (level == 4)
            book.recipe = new Recipe { B2 = "PAPER:8", B3 = "PAPER:8", C2 = "PAPER:8", C3 = "RABBIT_HIDE:8", count = 1 };
        return book;
    }

    [Fact]
    public void EnumerateRecipeCandidates_YieldsBothCraftingAndAnvil()
    {
        var luck4 = EnchantBookRecipes.PrepareCraftableBooks(Enumerable.Range(1, 5).Select(LuckLikeBook).ToList())
            .Single(b => b.internalname == "ENCHANTMENT_LUCK_4");
        var calculator = new CalculatorService(Substitute.For<IConfiguration>(), Substitute.For<IItemsApi>());

        var candidates = calculator.EnumerateRecipeCandidates(luck4).ToList();

        Assert.Equal(["crafting", "anvil"], candidates.Select(c => c.recipeType));
        var anvil = candidates[1].ingredients.Single();
        Assert.Equal("ENCHANTMENT_LUCK_3", anvil.ItemId);
        Assert.Equal(2, anvil.Count);
        Assert.True(CalculatorService.IsRecursiveCraftCandidate("anvil"));
    }

    [Fact]
    public async Task RecipeEndpoint_ReturnsGridForAnvilAndCraftedBooks()
    {
        // regression: /crafts/recipe only read NEU files, so ENCHANTMENT_* tags returned nothing
        var updater = new UpdaterService(null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdaterService>.Instance,
            null!, null!, null!, null!, null!, null!, null!);
        updater.Recipes = EnchantBookRecipes.PrepareCraftableBooks(Enumerable.Range(1, 5).Select(LuckLikeBook).ToList())
            .ToDictionary(b => b.internalname);
        var controller = new Coflnet.Sky.Crafts.Controllers.CraftsController(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Coflnet.Sky.Crafts.Controllers.CraftsController>.Instance,
            updater, new CraftingRecipeService(null!, null!), null!);

        var luck5 = await controller.GetRecipe("ENCHANTMENT_LUCK_5");
        Assert.Equal("ENCHANTMENT_LUCK_4:1", luck5.A1);
        Assert.Equal("ENCHANTMENT_LUCK_4:1", luck5.A2);
        Assert.Equal(2, luck5.GetIngredients().Count(i => !string.IsNullOrEmpty(i)));

        var luck4 = await controller.GetRecipe("ENCHANTMENT_LUCK_4");
        Assert.Equal("RABBIT_HIDE:8", luck4.C3); // NEU crafting grid wins over the anvil alternative
    }

    private sealed class BookPrices(IConfiguration config, IItemsApi api) : CalculatorService(config, api)
    {
        public List<string> Requested { get; } = new();
        protected override Task<PriceResponse> GetPriceFor(string itemTag, long count)
        {
            Requested.Add(itemTag);
            // enchant books are very expensive to buy, paper/hide are cheap
            var unit = itemTag.StartsWith("ENCHANTMENT_") ? 50_000_000 : 1;
            return Task.FromResult(new PriceResponse { BuyPrice = unit * count, SellPrice = unit, Available = (int)count, IsAh = true });
        }
    }

    [Fact]
    public async Task Luck5_CostsTwoCraftedLuck4_AndIsSoldUnderItsOwnTag()
    {
        var config = Substitute.For<IConfiguration>();
        var api = Substitute.For<IItemsApi>();
        api.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var calculator = new BookPrices(config, api);
        var books = EnchantBookRecipes.PrepareCraftableBooks(Enumerable.Range(1, 5).Select(LuckLikeBook).ToList());
        var lookup = books.ToDictionary(b => b.internalname);

        var luck4 = await calculator.GetCreaftingCost(lookup["ENCHANTMENT_LUCK_4"], lookup, new());
        var luck5 = await calculator.GetCreaftingCost(lookup["ENCHANTMENT_LUCK_5"], lookup, new());

        // Luck 4 is crafted from paper (32 coins of paper + hide), not bought for 50M or built from other books.
        Assert.Equal("crafting", luck4.Type);
        Assert.InRange(luck4.CraftCost, 32, 100);
        Assert.Equal("anvil", luck5.Type);
        Assert.False(luck5.Unobtainable);
        var ingredient = Assert.Single(luck5.Ingredients);
        Assert.Equal("ENCHANTMENT_LUCK_4", ingredient.ItemId);
        Assert.Equal(2, ingredient.Count);
        Assert.Equal("craft", ingredient.Type);
        // 2x the crafted cost, plus at most the per-step craft markup
        Assert.InRange(luck5.CraftCost, 2 * luck4.CraftCost, 2 * luck4.CraftCost * 1.1 + 100);
        Assert.Contains("ENCHANTMENT_LUCK_5", calculator.Requested);
        Assert.Equal("ENCHANTMENT_LUCK_5", luck5.ItemId);
        Assert.Equal("§9Luck 5", luck5.ItemName);
    }

    [Fact]
    public async Task AnvilBookFromUnobtainableBase_IsFlaggedUnobtainable()
    {
        var config = Substitute.For<IConfiguration>();
        var api = Substitute.For<IItemsApi>();
        api.ApiItemsNpccostGetAsync().Returns(new List<NpcCost>());
        var calculator = new NoMarket(config, api);
        // Luck 1 is missing from the lookup (table-only), Luck 2 needs two of it and nobody sells it.
        var books = EnchantBookRecipes.PrepareCraftableBooks(Enumerable.Range(1, 3).Select(LuckLikeBook).ToList());
        var lookup = books.ToDictionary(b => b.internalname);

        var luck2 = await calculator.GetCreaftingCost(lookup["ENCHANTMENT_LUCK_2"], lookup, new());

        Assert.Equal("anvil", luck2.Type);
        Assert.True(luck2.Unobtainable);
        Assert.True(double.IsFinite(luck2.CraftCost));
    }

    private sealed class NoMarket(IConfiguration config, IItemsApi api) : CalculatorService(config, api)
    {
        protected override Task<PriceResponse> GetPriceFor(string itemTag, long count)
            => Task.FromResult(new PriceResponse { BuyPrice = 0, SellPrice = 0, Available = 0, IsAh = true });
    }
}
