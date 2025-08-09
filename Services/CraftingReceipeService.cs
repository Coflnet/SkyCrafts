using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using System;
using System.Linq;
using Coflnet.Sky.Core.Services;

namespace Coflnet.Sky.Crafts.Services;

public class CraftingRecipeService(HypixelItemService itemService, PlayerState.Client.Api.IItemsApi itemsApi)
{
    public IEnumerable<ItemData> CraftAbleItems()
    {
        foreach (var itemPath in Directory.EnumerateFiles("itemData/items"))
        {
            ItemData item = null;
            try
            {
                item = JsonSerializer.Deserialize<ItemData>(File.ReadAllText(itemPath));
            }
            catch (Exception e)
            {
                Console.WriteLine("Error on: " + itemPath);
                Console.WriteLine(e);
            }
            // item needs to have a dedicated recipe
            if (item?.recipe != null || item?.recipes != null && item.recipes.Count > 0 && (item.recipes[0].type == "forge" || item.recipes[0].type == "crafting"))
                yield return item;
            if (itemPath.Contains("_NPC.json"))
            {
                var npc = JsonSerializer.Deserialize<NPC>(File.ReadAllText(itemPath));
                if (npc.recipes == null)
                {
                    continue;
                }
                foreach (var recipe in npc.recipes)
                {
                    yield return PortToItem(recipe);
                }
            }
        }
        var carpentry = JsonSerializer.Deserialize<Dictionary<string, List<Ingredient>>>(File.ReadAllText("Data/carpentry.json"));
        foreach (var recipe in carpentry)
        {
            yield return new ItemData()
            {
                itemid = recipe.Key,
                internalname = recipe.Key,
                displayname = recipe.Key,
                Type = "carpentry",
                recipes =
                    [
                        new NewRecipe()
                        {
                            type = "carpentry",
                            inputs = recipe.Value.ConvertAll(i => i.ItemId.Replace(":","-") + ":" + i.Count),
                            result = recipe.Key
                        }
                    ]
            };
        }
    }

    private static ItemData PortToItem(NPCRecipe recipe)
    {
        var parts = recipe.result.Split(':');
        if (parts.Length < 2)
        {
            Console.WriteLine($"Invalid NPC recipe result: {recipe.result} from {recipe.cost}");
            parts = new[] { parts[0], "1" }; // default to 1 if no count is specified
        }
        return new ItemData()
        {
            itemid = parts[0],
            internalname = parts[0],
            displayname = parts[0],
            Type = "npc",
            recipes =
                [
                    new NewRecipe()
                    {
                        type = recipe.type,
                        inputs = recipe.cost,
                        result = recipe.result,
                        count =  float.TryParse(parts[1], out var count) ? (int)count : 1
                    }
                ]
        };
    }

    public async Task<Recipe> GetRecipe(string id)
    {
        var itemData = await GetItemData(id);
        return itemData?.recipe ?? itemData?.recipes?.FirstOrDefault();
    }

    public async Task<ItemData> GetItemData(string id)
    {
        var path = $"itemData/items/{id}.json";
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<ItemData>(await File.ReadAllTextAsync(path));
    }

    internal async Task<IEnumerable<ItemData>> LoadExtraCraftable()
    {
        var items = await itemService.GetItemsAsync();
        return items.Where(i => i.Value.prestige != null).Select(i =>
        {
            var item = i.Value;
            var ingredients = item.UpgradeCosts.SelectMany(uc => uc).Concat(item.prestige.costs)
                .GroupBy(i => i.Type == "ESSENCE" ? $"ESSENCE_{i.EssenceType}" : i.ItemId)
                .Select(g => new Ingredient()
                {
                    ItemId = g.Key,
                    Count = g.Sum(i => i.Amount)
                }).ToList();
            var recipe = new NewRecipe()
            {
                inputs = ingredients.Select(i => i.ItemId.Replace(":", "-") + ":" + i.Count)
                    .Append("SKYBLOCK_COIN:15740000")
                    .Append(i.Key + ":1").ToList(),
                type = "malik",
                result = item.prestige.item_id,
            };
            return new ItemData()
            {
                internalname = recipe.result,
                displayname = items[recipe.result].Name,
                Type = "malik",
                recipes = [recipe]
            };
        });
    }
}
