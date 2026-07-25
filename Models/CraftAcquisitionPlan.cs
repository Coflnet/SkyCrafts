using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models;

public class AcquisitionFill
{
    public string Source { get; set; }
    public long Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double Cost { get; set; }
}

public class CraftAcquisitionPlan
{
    public string ItemId { get; set; }
    public long Quantity { get; set; }
    public double Cost { get; set; }
    public bool Enough { get; set; }
    public string Method { get; set; }
    public double DirectBuyCost { get; set; }
    public bool DirectBuyEnough { get; set; }
    public double CraftCost { get; set; }
    public bool CraftEnough { get; set; }
    public long CraftedQuantity { get; set; }
    public IReadOnlyList<AcquisitionFill> Purchases { get; set; } = Array.Empty<AcquisitionFill>();
    public IReadOnlyList<CraftAcquisitionPlan> Ingredients { get; set; } = Array.Empty<CraftAcquisitionPlan>();
}
