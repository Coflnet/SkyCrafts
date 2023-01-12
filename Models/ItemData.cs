using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models;
public class ItemData
{
    public string itemid { get; set; }
    public string displayname { get; set; }
    public string nbttag { get; set; }
    public int damage { get; set; }
    public List<string> lore { get; set; }
    public Recipe recipe { get; set; }
    public List<NewRecipe> recipes { get; set; }
    public string internalname { get; set; }
    public string clickcommand { get; set; }
    public string modver { get; set; }
    public bool useneucraft { get; set; }
    public string infoType { get; set; }
    public List<string> info { get; set; }
    public string crafttext { get; set; }
    public string slayer_req { get; set; }

    public IEnumerable<string> GetIngredients()
    {
        if (recipe != null)
            return recipe.GetIngredients();
        else if (recipes != null && recipes.Count > 0 && recipes[0].type == "forge")
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
}