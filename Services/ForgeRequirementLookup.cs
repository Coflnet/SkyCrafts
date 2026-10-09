using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Crafts.Models;
using Newtonsoft.Json;

namespace Coflnet.Sky.Crafts.Services;
#nullable enable
/// <summary>
/// Single source for what a forge item requires (Heart of the Mountain tier, collections). Backed by
/// Data/forge_requirements.json, with the NEU "crafttext" ("Requires: HotM 5") as fallback for the tier.
/// </summary>
public static class ForgeRequirementLookup
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Requirements = new(Load);

    /// <summary>
    /// Display name reduced to a comparable key: colour codes removed, then only letters, digits, spaces and
    /// hyphens kept (so "Divan's Drill" matches regardless of the apostrophe). Applied to the file keys too.
    /// </summary>
    public static string Clean(string? displayName)
        => Regex.Replace(displayName ?? "", @"§\w|[^-a-zA-Z0-9 ]", "").Trim();

    /// <summary>Requirements keyed by name ("HotM" for the tier), or null when neither the file nor crafttext knows the item.</summary>
    public static Dictionary<string, string>? GetRequirements(ItemData item)
    {
        var found = Requirements.Value.GetValueOrDefault(Clean(item.displayname));
        if (found?.ContainsKey("HotM") ?? false)
            return found;
        var match = Regex.Match(item.crafttext ?? "", @"HotM\s+(\d+)");
        if (!match.Success)
            return found;
        return new Dictionary<string, string>(found ?? new()) { ["HotM"] = match.Groups[1].Value };
    }

    /// <summary>Required Heart of the Mountain tier, or null when unknown.</summary>
    public static int? GetHotmTier(ItemData item)
        => GetRequirements(item)?.GetValueOrDefault("HotM") is { } level && int.TryParse(level, out var tier) ? tier : null;

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var path = File.Exists("Data/forge_requirements.json") ? "Data/forge_requirements.json"
            : Path.Combine(AppContext.BaseDirectory, "Data/forge_requirements.json");
        var parsed = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(path))
            ?? throw new Exception("Failed to parse forge requirements");
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsed)
        {
            var list = item.Value.Select(s => s.Replace("Heart of the Mountain Tier", "HotM")).ToList();
            result[Clean(item.Key)] = list.Select(SplitLevel).ToDictionary(r => r.name, r => r.level);
        }
        return result;
    }

    /// <summary>
    /// Splits "Umber Collection III" into name and numeric level (arabic or roman). Requirements without a
    /// level ("Talk to Dulin") keep their full text as name with level 1, so every level parses as a number.
    /// </summary>
    internal static (string name, string level) SplitLevel(string requirement)
    {
        var split = requirement.LastIndexOf(' ');
        if (split < 0)
            return (requirement, "1");
        var last = requirement[(split + 1)..];
        if (int.TryParse(last, out _))
            return (requirement[..split], last);
        if (TryParseRoman(last, out var roman))
            return (requirement[..split], roman.ToString());
        return (requirement, "1");
    }

    private static bool TryParseRoman(string text, out int value)
    {
        value = 0;
        var previous = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var digit = text[i] switch { 'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, _ => 0 };
            if (digit == 0)
                return false;
            value += digit < previous ? -digit : digit;
            previous = Math.Max(previous, digit);
        }
        return text.Length > 0;
    }
}
