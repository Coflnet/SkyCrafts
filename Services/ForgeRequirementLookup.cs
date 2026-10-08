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
            // split on last space to get the level
            result[Clean(item.Key)] = list.ToDictionary(s => string.Join(' ', s.Split(" ").Reverse().Skip(1).Reverse()), s => s.Split(" ").Last());
        }
        return result;
    }
}
