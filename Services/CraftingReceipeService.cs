using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using System;

namespace Coflnet.Sky.Crafts.Services
{
    public class CraftingRecipeService
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
                catch (System.Exception e)
                {
                    Console.WriteLine("Error on: " + itemPath);
                    Console.WriteLine(e);
                }
                // item needs to have a dedicated recipe
                if (item?.recipe != null || item?.recipes != null && item.recipes.Count > 0 && item.recipes[0].type == "forge")
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
                    recipes = new List<NewRecipe>()
                    {
                        new NewRecipe()
                        {
                            type = "carpentry",
                            inputs = recipe.Value.ConvertAll(i => i.ItemId.Replace(":","-") + ":" + i.Count),
                            result = recipe.Key
                        }
                    }
                };
            }
        }

        private static ItemData PortToItem(NPCRecipe recipe)
        {
            return new ItemData()
            {
                itemid = recipe.result,
                internalname = recipe.result,
                displayname = recipe.result,
                Type = "npc",
                recipes = new List<NewRecipe>()
                            {
                                new NewRecipe()
                                {
                                    type = recipe.type,
                                    inputs = recipe.cost,
                                    result = recipe.result
                                }
                            }
            };
        }

        public async Task<Recipe> GetRecipe(string id)
        {
            var itemData = await GetItemData(id);
            return itemData?.recipe;
        }

        public async Task<ItemData> GetItemData(string id)
        {
            var path = $"itemData/items/{id}.json";
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<ItemData>(await File.ReadAllTextAsync(path));
        }
    }
}
