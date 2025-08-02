using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services;
public partial class RequirementService
{
    private CollectionService collectionService;
    private IItemsApi itemsApi;
    private ILogger<RequirementService> logger;
    private ConcurrentDictionary<string, (RequiredSkill, DateTime)> skillCache = new();
    private HypixelItemService hypixelItemService;
    public RequirementService(CollectionService collectionService, IItemsApi itemsApi, ILogger<RequirementService> logger, HypixelItemService hypixelItemService)
    {
        this.collectionService = collectionService;
        this.itemsApi = itemsApi;
        this.logger = logger;
        this.hypixelItemService = hypixelItemService;
    }

    [GeneratedRegex(@"§[\da-f]")]
    private static partial Regex MinecraftFormatRemoveRegex();
    public async Task AssignRequirements(ItemData item, ProfitableCraft result, System.Collections.Generic.Dictionary<string, ProfitableCraft> crafts)
    {
        if (result.ReqCollection == default || result.Ingredients.Any(i => i?.Type == "craft"))
        {
            var name = MinecraftFormatRemoveRegex().Replace(item.displayname, "");
            result.ReqCollection = await collectionService.GetRequiredCollection(name);
            foreach (var ingridient in result.Ingredients.Where(i => i?.Type == "craft"))
            {
                if (crafts.TryGetValue(ingridient.ItemId, out var subItem) && subItem.ReqCollection != default && result.ReqCollection == default)
                {
                    result.ReqCollection = subItem.ReqCollection;
                }
            }
        }
        var items = await hypixelItemService.GetItemsAsync();
        if (result.ReqSkill == default)
        {
            try
            {
                if (skillCache.TryGetValue(result.ItemId, out var cache) && cache.Item2.AddHours(1) > DateTime.UtcNow)
                {
                    result.ReqSkill = cache.Item1;
                }
                else
                {
                    await AssignSkillRequirement(item, result);
                    skillCache[result.ItemId] = (result.ReqSkill, DateTime.UtcNow);
                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e, $"Error while assigning skill requirement for {item.itemid}");
            }
        }
        if (!string.IsNullOrEmpty(item.slayer_req))
        {
            var level = int.Parse(item.slayer_req.Split("_").Last());
            if (!string.IsNullOrEmpty(item.crafttext))
            {
                var craftText = Regex.Match(item.crafttext, @"Slayer (\d*)");
                if (craftText.Success)
                    level = int.Parse(craftText.Groups[1].Value);
            }
            result.ReqSlayer = new Models.RequiredCollection()
            {
                Name = item.slayer_req.Split("_").First(),
                Level = level
            };
        }
        else
        {
            if (items.TryGetValue(item.internalname, out var hypixelData) && hypixelData.Requirements.Any(r => r.Type == "SLAYER"))
            {
                var slayerReq = hypixelData.Requirements.FirstOrDefault(r => r.Type == "SLAYER");
                if (slayerReq != null)
                {
                    result.ReqSlayer = new RequiredCollection()
                    {
                        Name = slayerReq.SlayerBossType,
                        Level = slayerReq.Level
                    };
                    return;
                }
            }
            var SlayerLine = item.lore?.Where(l => l.Contains("Slayer ")).FirstOrDefault();
            if (SlayerLine != null)
            {
                var match = Regex.Match(MinecraftFormatRemoveRegex().Replace(SlayerLine, ""), @"☠ Requires (.*) Slayer (\d)");
                if (match.Success)
                {
                    result.ReqSlayer = new RequiredCollection()
                    {
                        Name = match.Groups[1].Value,
                        Level = int.Parse(match.Groups[2].Value)
                    };
                }

            }
        }
    }

    private async Task AssignSkillRequirement(ItemData item, ProfitableCraft result)
    {
        var recipes = await itemsApi.ApiItemsRecipeTagGetAsync(result.ItemId);
        if (recipes.Count == 0)
            return;
        var recipe = recipes.OrderByDescending(r => r.Requirements.Count).First();
        var matchingSkill = recipe.Requirements.FirstOrDefault(r => r.Contains("Skill "));
        if (matchingSkill == null)
            return;
        var match = Regex.Match(matchingSkill, @"§a(.*) Skill (\d+)");
        result.ReqSkill = new RequiredSkill()
        {
            Name = match.Groups[1].Value,
            Level = int.Parse(match.Groups[2].Value)
        };
        logger.LogInformation($"Found skill requirement for {result.ItemId} {result.ReqSkill.Name} {result.ReqSkill.Level}");

    }
}
