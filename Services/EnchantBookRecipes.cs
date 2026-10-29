using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Crafts.Models;

namespace Coflnet.Sky.Crafts.Services;

/// <summary>
/// Helpers for enchanted books: normalizes NEU tags (<c>LUCK;4</c>) to the market tag
/// (<c>ENCHANTMENT_LUCK_4</c>), extracts the real display name and derives anvil combination
/// recipes (two books of level L-1 make one of level L) from the lore "Source" section.
/// </summary>
public static class EnchantBookRecipes
{
    public const string BookItemId = "minecraft:enchanted_book";
    public const string AnvilType = "anvil";
    private const string TagPrefix = "ENCHANTMENT_";
    private static readonly Regex SourceLine = new(@"^§a([IVXLCDM]+)(?:-([IVXLCDM]+))?: ", RegexOptions.Compiled);

    /// <summary>True for NEU enchanted book items that carry a <c>NAME;LVL</c> internalname.</summary>
    public static bool IsLevelBook(ItemData item)
        => item?.itemid == BookItemId && item.internalname != null && item.internalname.Contains(';');

    /// <summary>"LUCK;4" -> "ENCHANTMENT_LUCK_4".</summary>
    public static string ToMarketTag(string neuInternalName) => TagPrefix + neuInternalName.Replace(';', '_');

    /// <summary>Splits "LUCK;4" or "ENCHANTMENT_LUCK_4" into name "LUCK" and level 4.</summary>
    public static bool TryParseTag(string tag, out string name, out int level)
    {
        name = null;
        level = 0;
        if (string.IsNullOrEmpty(tag))
            return false;
        if (tag.StartsWith(TagPrefix))
            tag = tag.Substring(TagPrefix.Length);
        var split = tag.LastIndexOfAny([';', '_']);
        if (split <= 0 || !int.TryParse(tag.AsSpan(split + 1), out level))
            return false;
        name = tag.Substring(0, split);
        return true;
    }

    /// <summary>
    /// First meaningful lore line, e.g. "§9Luck IV" or "§d§lUltimate Wise I". The leading
    /// "§8Combinable in Anvil" line and blank lines are skipped.
    /// </summary>
    public static string ParseDisplayName(IEnumerable<string> lore)
        => lore?.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("Combinable in Anvil"));

    /// <summary>Applies tag normalization and display name to a level book in place.</summary>
    public static void Normalize(ItemData book)
    {
        book.displayname = ParseDisplayName(book.lore) ?? book.displayname;
        book.internalname = ToMarketTag(book.internalname);
    }

    /// <summary>Parses roman numerals (I..MMMM); returns 0 when invalid.</summary>
    public static int ParseRoman(string roman)
    {
        if (string.IsNullOrEmpty(roman))
            return 0;
        var total = 0;
        var previous = 0;
        for (var i = roman.Length - 1; i >= 0; i--)
        {
            var value = roman[i] switch
            {
                'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, 'C' => 100, 'D' => 500, 'M' => 1000,
                _ => 0
            };
            if (value == 0)
                return 0;
            total += value < previous ? -value : value;
            previous = Math.Max(previous, value);
        }
        return total;
    }

    /// <summary>
    /// Level ranges from the lore "§6Source:" section. Lines not starting with
    /// "§a&lt;roman&gt;[-&lt;roman&gt;]: " are continuation lines and ignored.
    /// </summary>
    public static List<(int from, int to)> ParseSourceRanges(IEnumerable<string> lore)
    {
        var ranges = new List<(int, int)>();
        var inSource = false;
        foreach (var line in lore ?? Enumerable.Empty<string>())
        {
            if (!inSource)
            {
                inSource = line != null && line.StartsWith("§6Source:");
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
                break;
            var match = SourceLine.Match(line);
            if (!match.Success)
                continue;
            var from = ParseRoman(match.Groups[1].Value);
            var to = match.Groups[2].Success ? ParseRoman(match.Groups[2].Value) : from;
            if (from > 0 && to >= from)
                ranges.Add((from, to));
        }
        return ranges;
    }

    /// <summary>
    /// Levels (L &gt;= 2) that can be made in an anvil from two books of level L-1, keyed by enchant name.
    /// Level L qualifies iff some source range [a,b] of ANY level file has a &lt; L &lt;= b and both L-1 and L exist.
    /// Books are expected as <c>(name, level, lore)</c>.
    /// </summary>
    public static Dictionary<string, SortedSet<int>> ComputeAnvilLevels(IEnumerable<(string name, int level, IEnumerable<string> lore)> books)
    {
        var result = new Dictionary<string, SortedSet<int>>();
        foreach (var group in books.GroupBy(b => b.name))
        {
            var existing = group.Select(b => b.level).ToHashSet();
            var ranges = group.SelectMany(b => ParseSourceRanges(b.lore)).Distinct().ToList();
            var levels = existing.Where(l => l >= 2 && existing.Contains(l - 1) && ranges.Any(r => r.from < l && l <= r.to));
            var set = new SortedSet<int>(levels);
            if (set.Count > 0)
                result[group.Key] = set;
        }
        return result;
    }

    /// <summary>
    /// Pricing uses <see cref="NewRecipe.inputs"/>. The grid slots A1/A2 (one book each, like the two
    /// anvil slots) are only for /crafts/recipe consumers, which render a crafting grid.
    /// </summary>
    public static NewRecipe CreateAnvilRecipe(string name, int level) => new()
    {
        type = AnvilType,
        inputs = [$"{TagPrefix}{name}_{level - 1}:2"],
        A1 = $"{TagPrefix}{name}_{level - 1}:1",
        A2 = $"{TagPrefix}{name}_{level - 1}:1",
        result = $"{TagPrefix}{name}_{level}",
        count = 1
    };

    /// <summary>
    /// Normalizes the given level books (tags, names) and attaches anvil recipes. Returns only books
    /// that are craftable (a NEU crafting recipe and/or an anvil recipe).
    /// </summary>
    public static List<ItemData> PrepareCraftableBooks(IEnumerable<ItemData> levelBooks)
    {
        var books = levelBooks.ToList();
        var parsed = books.Select(b => TryParseTag(b.internalname, out var n, out var l) ? (name: n, level: l, lore: (IEnumerable<string>)b.lore) : default).ToList();
        var anvil = ComputeAnvilLevels(parsed.Where(p => p.name != null));
        var craftable = new List<ItemData>();
        for (var i = 0; i < books.Count; i++)
        {
            var book = books[i];
            var (name, level, _) = parsed[i];
            if (name == null)
                continue;
            Normalize(book);
            var hasCrafting = book.recipe != null || book.recipes is { Count: > 0 } && book.recipes.Any(r => r.type == "crafting");
            var hasAnvil = anvil.TryGetValue(name, out var levels) && levels.Contains(level);
            if (hasAnvil)
            {
                book.recipes = (book.recipes ?? []).Append(CreateAnvilRecipe(name, level)).ToList();
            }
            if (hasCrafting || hasAnvil)
                craftable.Add(book);
        }
        return craftable;
    }
}
