using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models;
public class ItemData
{
    public string itemid { get; set; }
    public string displayname { get; set; }
    public List<string> lore { get; set; }
    public Recipe recipe { get; set; }
    public List<NewRecipe> recipes { get; set; }
    public string internalname { get; set; }
    public string slayer_req { get; set; }
    public string Crafttext { get; set; }
    public string Type { get; set; }

    public IEnumerable<string> GetIngredients()
    {
        if (recipe != null)
            return recipe.GetIngredients();
        else if (recipes != null && recipes.Count > 0 && (recipes[0].type == "forge" || recipes[0].type == "npc_shop" || recipes[0].type == "carpentry"))
            return recipes[0].inputs;
        return new List<string>();
    }
}

public class NewRecipe
{
    public string type { get; set; }
    public List<string> inputs { get; set; }
    public int count { get; set; }
    public string overrideOutputId { get; set; }
    public int duration { get; set; }
    public string result { get; set; }
}

public class NPC
{
    public List<NPCRecipe> recipes { get; set; }
}

public class NPCRecipe : IGetIngredients
{
    public string type { get; set; }
    public string result { get; set; }
    public List<string> cost { get; set; }

    public IEnumerable<string> GetIngredients()
    {
        return cost;
    }
}