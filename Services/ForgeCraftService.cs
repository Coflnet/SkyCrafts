using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.IO;
using Newtonsoft.Json;
using System;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.Crafts.Services;
#nullable enable
public class ForgeCraftService
{
    private IConfiguration config;
    private ILogger<ForgeCraftService> logger;
    private Dictionary<string, Dictionary<string, string>> Requirements = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ForgeFlip> Flips = new();
    public IEnumerable<ForgeFlip> FlipList => Flips.Values;

    public ForgeCraftService(IConfiguration config, ILogger<ForgeCraftService> logger)
    {
        this.config = config;
        this.logger = logger;
    }

    public async Task Update(Dictionary<string, ProfitableCraft> crafts, List<ItemData> craftable)
    {
        var forgeItems = crafts.Values.Where(c => c.Type == "forge").ToList();
        var forgeItemLookup = forgeItems.GroupBy(l => l.ItemId)
            .Select(s => s.First()).ToDictionary(c => c.ItemId, c => c);
        var timeLookup = craftable.Where(c => forgeItemLookup.ContainsKey(c.internalname))
            .GroupBy(l => l.internalname).Select(s => s.OrderByDescending(w => (w.Type == "npc") ? -1 : 0).First())
            .ToDictionary(c => c.internalname, c => c);
        if (Requirements.Count == 0)
        {
            var stringRequirements = File.ReadAllText("Data/forge_requirements.json");
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(stringRequirements) ?? throw new Exception("Failed to parse forge requirements");
            foreach (var item in parsed)
            {
                var list = item.Value.Select(s => s.Replace("Heart of the Mountain Tier", "HotM")).ToList();
                // split on last space to get the level
                Requirements[item.Key] = list.ToDictionary(s => string.Join(' ', s.Split(" ").Reverse().Skip(1).Reverse()), s => s.Split(" ").Last());
            }
        }
        foreach (var item in forgeItems)
        {
            try
            {
                UpdateItem(timeLookup, item);
            }
            catch (System.Exception e)
            {
                logger.LogError(e, $"Failed to update {item.ItemId}");
            }
        }
    }

    private void UpdateItem(Dictionary<string, ItemData> timeLookup, ProfitableCraft? item)
    {
        var itemData = timeLookup[item.ItemId];
        var time = itemData.recipes[0].duration;
        var requiredLevel = 0;
        var cleanedName = Regex.Replace(itemData.displayname, @"§\w|[^-a-zA-Z ]", "").Trim();
        var forgeRequirements = Requirements.GetValueOrDefault(cleanedName);
        if (forgeRequirements?.TryGetValue("HotM", out string? level) ?? false)
        {
            requiredLevel = int.Parse(level);
        }
        if (forgeRequirements == null)
        {
            logger.LogWarning($"No requirements found for {item.ItemId} {cleanedName}");
        }
        if (item.ItemId == "REFINED_MITHRIL_PICKAXE")
            Console.WriteLine($"Updating {item.ItemId} {cleanedName} {time} {requiredLevel}");
        Flips[item.ItemId] = new ForgeFlip()
        {
            CraftData = item,
            Duration = time,
            RequiredHotMLevel = requiredLevel,
            ProfitPerHour = Math.Clamp((item.SellPrice - item.CraftCost) / time * 3600, 0, int.MaxValue),
            Requirements = forgeRequirements?.ToDictionary(r => r.Key, r => int.Parse(r.Value))
        };
    }
}


public class ForgeFlip
{
    public ProfitableCraft CraftData { get; set; }
    public int Duration { get; set; }
    public int RequiredHotMLevel { get; set; }
    public double ProfitPerHour { get; set; }
    public Dictionary<string, int>? Requirements { get; set; }
}