using CoreTier = Coflnet.Sky.Core.Tier;

namespace Coflnet.Sky.Crafts.Models;

public readonly record struct PetKey(string BaseTag, CoreTier Rarity)
{
    public override string ToString() => $"{BaseTag}:{Rarity}";
}
