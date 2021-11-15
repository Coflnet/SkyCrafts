using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;

namespace Coflnet.Sky.Crafts.Services
{
    public class CraftingRecipeService
    {
        public IEnumerable<ItemData> CraftAbleItems()
        {
            foreach (var itemPath in Directory.EnumerateFiles("itemData/items"))
            {
                var item = JsonSerializer.Deserialize<ItemData>(File.ReadAllText(itemPath));
                // item needs to have a dedicated recipe
                if (item.recipe != null)
                    yield return item;
            }
        }

        public async Task<Dictionary<string, string>> GetRecipe(string id)
        {
            var path = $"itemData/items/{id}.json";
            if(!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<ItemData>(await File.ReadAllTextAsync(path)).recipe;
        }
    }
}
