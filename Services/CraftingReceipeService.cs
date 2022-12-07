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
                if (item?.recipe != null)
                    yield return item;
            }
        }

        public async Task<Recipe> GetRecipe(string id)
        {
            var path = $"itemData/items/{id}.json";
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<ItemData>(await File.ReadAllTextAsync(path)).recipe;
        }
    }
}
