using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Coflnet.Sky.Crafts.Models;

namespace Coflnet.Sky.Crafts.Services
{
    public class CraftingReceipeService
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
    }
}
