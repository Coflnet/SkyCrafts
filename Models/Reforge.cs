using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models;

public class Reforge
{
    public string InternalName { get; set; }
    public string ReforgeName { get; set; }
    public string ReforgeType { get; set; }
    public string ItemTypes { get; set; }
    public string[] RequiredRarities { get; set; }
    public Dictionary<string, int> ReforgeCosts { get; set; }
    public Dictionary<string, Dictionary<string, double>> ReforgeStats { get; set; }
}
