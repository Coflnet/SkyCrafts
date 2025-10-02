using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Microsoft.Extensions.Logging;
using CoreTier = Coflnet.Sky.Api.Client.Model.Tier;
using ItemsItem = Coflnet.Sky.Items.Client.Model.Item;

namespace Coflnet.Sky.Crafts.Services;

public sealed class GeorgePetOfferService
{
    private static readonly IReadOnlyDictionary<string, CoreTier> tierNameLookup = Enum.GetValues(typeof(CoreTier))
        .Cast<CoreTier>()
        .ToDictionary(tier => NormalizeTierKey(tier.ToString()), tier => tier);

    private static readonly IReadOnlyDictionary<CoreTier, int> tierNumericOrder = new Dictionary<CoreTier, int>
    {
        { ResolveTier("COMMON"), 0 },
        { ResolveTier("UNCOMMON"), 1 },
        { ResolveTier("RARE"), 2 },
        { ResolveTier("EPIC"), 3 },
        { ResolveTier("LEGENDARY"), 4 },
        { ResolveTier("MYTHIC"), 5 },
        { ResolveTier("DIVINE"), 6 },
        { ResolveTier("SPECIAL"), 7 },
        { ResolveTier("VERY_SPECIAL"), 8 },
        { ResolveTier("ULTIMATE"), 9 }
    };

    private static readonly IReadOnlyDictionary<int, CoreTier> numericToTier = tierNumericOrder.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<CoreTier, double>> basePriceTable = BuildGeorgePetPrices();
    private readonly IItemsApi itemsApi;
    private readonly ILogger<GeorgePetOfferService> logger;
    private readonly TimeSpan cacheDuration = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim cacheSemaphore = new(1, 1);
    private GeorgePetPriceSnapshot snapshot = GeorgePetPriceSnapshot.Empty;
    private DateTime lastUpdated = DateTime.MinValue;

    public GeorgePetOfferService(IItemsApi itemsApi, ILogger<GeorgePetOfferService> logger)
    {
        this.itemsApi = itemsApi;
        this.logger = logger;
    }

    private static string NormalizeTierKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var filtered = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(filtered).ToUpperInvariant();
    }

    private static CoreTier ResolveTier(string value)
    {
        var key = NormalizeTierKey(value);
        if (tierNameLookup.TryGetValue(key, out var tier))
            return tier;

        throw new InvalidOperationException($"Unknown tier '{value}' for Coflnet.Sky.Api.Client.Model.Tier");
    }

    public async Task<GeorgePetPriceSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && IsCacheFresh)
            return snapshot;

        var items = await itemsApi.ItemsGetAsync();
        return await GetSnapshotAsync(items, forceRefresh, cancellationToken);
    }

    public async Task<GeorgePetPriceSnapshot> GetSnapshotAsync(IEnumerable<ItemsItem> items, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && IsCacheFresh)
            return snapshot;

    var itemList = items?.Where(i => i != null).ToList() ?? new List<ItemsItem>();
        await cacheSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && IsCacheFresh)
                return snapshot;

            var offersByKey = new Dictionary<PetKey, double>();
            var offersByTag = new Dictionary<string, PetOffer>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in itemList)
            {
                if (item.Tag == null)
                    continue;

                if (!TryGetItemRarity(item, out var rarity))
                    continue;

                var baseTag = NormalizePetTag(item.Tag);
                if (!basePriceTable.TryGetValue(baseTag, out var rarityPrices))
                    continue;

                if (!rarityPrices.TryGetValue(rarity, out var price))
                    continue;

                var key = new PetKey(baseTag, rarity);
                offersByKey[key] = price;
                offersByTag[item.Tag] = new PetOffer(item.Tag, key, price);
            }

            snapshot = new GeorgePetPriceSnapshot(offersByKey, offersByTag);
            lastUpdated = DateTime.UtcNow;
            return snapshot;
        }
        finally
        {
            cacheSemaphore.Release();
        }
    }

    public bool TryGetPriceFromCache(string itemTag, out double price)
    {
        return snapshot.TryGetPrice(itemTag, out price);
    }

    public async Task<double?> TryGetPriceAsync(string itemTag, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var currentSnapshot = await GetSnapshotAsync(forceRefresh, cancellationToken);
        if (currentSnapshot.TryGetPrice(itemTag, out var price))
            return price;
        return null;
    }

    public static IReadOnlyCollection<PetOffer> CreateMockOffers()
    {
        var offers = new List<PetOffer>();
        foreach (var (baseTag, rarityMap) in basePriceTable)
        {
            foreach (var (rarity, price) in rarityMap)
            {
                var itemTag = BuildMockItemTag(baseTag, rarity);
                offers.Add(new PetOffer(itemTag, new PetKey(baseTag, rarity), price));
            }
        }
        return offers;
    }

    private static string BuildMockItemTag(string baseTag, CoreTier rarity)
    {
        var numeric = tierNumericOrder.TryGetValue(rarity, out var value) ? value : (int)rarity;
        return $"PET_{baseTag};{numeric}";
    }

    private bool IsCacheFresh
        => snapshot.OffersByKey.Count > 0 && DateTime.UtcNow - lastUpdated < cacheDuration;

    private static bool TryGetItemRarity(ItemsItem item, out CoreTier rarity)
    {
        rarity = default;
        if (item == null)
            return false;

        foreach (var propertyName in new[] { "Rarity", "Tier", "PetTier" })
        {
            var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null)
                continue;

            var value = property.GetValue(item);
            if (TryNormalizeRarity(value, out rarity))
                return true;
        }

        return TryInferRarityFromTag(item.Tag, out rarity);
    }

    private static bool TryNormalizeRarity(object value, out CoreTier rarity)
    {
        rarity = default;
        if (value == null)
            return false;

        switch (value)
        {
            case string stringValue:
                return TryParseRarityString(stringValue, out rarity);
            case Enum enumValue:
                return TryParseRarityString(enumValue.ToString(), out rarity);
            case IConvertible convertible:
                try
                {
                    var numeric = convertible.ToInt32(null);
                    return TryParseRarityNumeric(numeric, out rarity);
                }
                catch
                {
                    return false;
                }
            default:
                return TryParseRarityString(value.ToString(), out rarity);
        }
    }

    private static bool TryParseRarityString(string value, out CoreTier rarity)
    {
        rarity = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = NormalizeTierKey(value);
        if (tierNameLookup.TryGetValue(normalized, out var parsed))
            return Assign(parsed, out rarity);

        return false;
    }

    private static bool TryParseRarityNumeric(int value, out CoreTier rarity)
    {
        rarity = default;
        if (numericToTier.TryGetValue(value, out var tier))
            return Assign(tier, out rarity);
        return false;
    }

    private static bool TryInferRarityFromTag(string tag, out CoreTier rarity)
    {
        rarity = default;
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var semicolonIndex = tag.IndexOf(';');
        if (semicolonIndex >= 0 && semicolonIndex < tag.Length - 1)
        {
            var suffix = tag[(semicolonIndex + 1)..];
            if (int.TryParse(suffix, out var numeric) && TryParseRarityNumeric(numeric, out rarity))
                return true;

            if (TryParseRarityString(suffix, out rarity))
                return true;
        }

        foreach (var rarityCandidate in Enum.GetValues(typeof(CoreTier)).Cast<CoreTier>())
        {
            var suffix = "_" + rarityCandidate.ToString().ToUpperInvariant();
            if (tag.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                rarity = rarityCandidate;
                return true;
            }
        }

        return false;
    }

    private static bool Assign(CoreTier rarityValue, out CoreTier rarity)
    {
        rarity = rarityValue;
        return true;
    }

    private static string NormalizePetTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return string.Empty;

        var baseTag = tag;
        var semicolonIndex = baseTag.IndexOf(';');
        if (semicolonIndex >= 0)
            baseTag = baseTag[..semicolonIndex];
        if (baseTag.StartsWith("PET_", StringComparison.OrdinalIgnoreCase))
            baseTag = baseTag[4..];
        if (baseTag.EndsWith("_PET", StringComparison.OrdinalIgnoreCase))
            baseTag = baseTag[..^4];
        return baseTag.ToUpperInvariant();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<CoreTier, double>> BuildGeorgePetPrices()
    {
        var map = new Dictionary<string, IReadOnlyDictionary<CoreTier, double>>(StringComparer.OrdinalIgnoreCase);

        var travelingZoo = CreatePriceMap((CoreTier.COMMON, 5_000), (CoreTier.UNCOMMON, 12_500), (CoreTier.RARE, 50_000), (CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 5_000_000));
        var craftable = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 500), (CoreTier.RARE, 1_000), (CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 5_000), (CoreTier.MYTHIC, 10_000));
        var mobDrops = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 500), (CoreTier.RARE, 2_000), (CoreTier.EPIC, 10_000), (CoreTier.LEGENDARY, 1_000_000), (CoreTier.MYTHIC, 2_500_000));
        var rareMobDrops = CreatePriceMap((CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 100_000));
        var slugPrices = CreatePriceMap((CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 5_000_000));
        var tarantulaPrices = CreatePriceMap((CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 100_000), (CoreTier.MYTHIC, 150_000));
        var phoenixPrices = CreatePriceMap((CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 5_000_000));
        var milestone = CreatePriceMap((CoreTier.COMMON, 10_000), (CoreTier.UNCOMMON, 50_000), (CoreTier.RARE, 500_000), (CoreTier.EPIC, 2_500_000), (CoreTier.LEGENDARY, 10_000_000));
        var darkAuction = CreatePriceMap((CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 5_000_000));
        var ammonite = CreatePriceMap((CoreTier.LEGENDARY, 5_000));
        var armadillo = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 500), (CoreTier.RARE, 1_000), (CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 5_000));
        var bal = CreatePriceMap((CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 5_000));
        var bee = CreatePriceMap((CoreTier.COMMON, 2_500), (CoreTier.UNCOMMON, 5_000), (CoreTier.RARE, 25_000), (CoreTier.EPIC, 100_000), (CoreTier.LEGENDARY, 325_000));
        var blackCat = CreatePriceMap((CoreTier.LEGENDARY, 5_000_000), (CoreTier.MYTHIC, 10_000_000));
        var enderDragon = CreatePriceMap((CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 5_000_000));
        var flyingFish = CreatePriceMap((CoreTier.EPIC, 100_000), (CoreTier.LEGENDARY, 250_000), (CoreTier.MYTHIC, 10_000));
        var goldenDragon = CreatePriceMap((CoreTier.LEGENDARY, 5_000));
        var golem = CreatePriceMap((CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 2_500_000));
        var grandmaWolf = CreatePriceMap((CoreTier.COMMON, 5_000), (CoreTier.UNCOMMON, 10_000), (CoreTier.RARE, 25_000), (CoreTier.EPIC, 50_000), (CoreTier.LEGENDARY, 200_000));
        var griffin = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 500), (CoreTier.RARE, 1_000), (CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 2_500_000));
        var guardian = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 500), (CoreTier.RARE, 1_000), (CoreTier.EPIC, 100_000), (CoreTier.LEGENDARY, 250_000), (CoreTier.MYTHIC, 500_000));
        var kuudra = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 100), (CoreTier.RARE, 1_000), (CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 5_000));
        var megalodon = CreatePriceMap((CoreTier.EPIC, 500_000), (CoreTier.LEGENDARY, 2_500_000));
        var mithrilGolem = CreatePriceMap((CoreTier.COMMON, 5_000), (CoreTier.UNCOMMON, 10_000), (CoreTier.RARE, 25_000), (CoreTier.EPIC, 50_000), (CoreTier.LEGENDARY, 200_000), (CoreTier.MYTHIC, 10_000));
        var rat = CreatePriceMap((CoreTier.LEGENDARY, 5_000));
        var riftFerret = CreatePriceMap((CoreTier.EPIC, 50_000));
        var scatha = CreatePriceMap((CoreTier.RARE, 1_000), (CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 5_000));
        var skeletonHorse = CreatePriceMap((CoreTier.LEGENDARY, 500_000));
        var spirit = CreatePriceMap((CoreTier.EPIC, 2_000), (CoreTier.LEGENDARY, 5_000));
        var squid = CreatePriceMap((CoreTier.COMMON, 100), (CoreTier.UNCOMMON, 500), (CoreTier.RARE, 100_000), (CoreTier.EPIC, 200_000), (CoreTier.LEGENDARY, 500_000));

        void AddPets(IReadOnlyDictionary<CoreTier, double> priceMap, params string[] pets)
        {
            foreach (var pet in pets)
            {
                if (!string.IsNullOrEmpty(pet))
                    map[pet] = priceMap;
            }
        }

        AddPets(travelingZoo, "BLUE_WHALE", "ELEPHANT", "GIRAFFE", "LION", "MONKEY", "TIGER");
        AddPets(craftable, "BAT", "BLAZE", "CHICKEN", "ENDERMITE", "HORSE", "JERRY", "MOOSHROOM_COW", "OCELOT", "PIG", "PIGMAN", "RABBIT", "SHEEP", "SILVERFISH", "SKELETON", "SNAIL", "SPIDER", "WITHER_SKELETON", "WOLF", "ZOMBIE");
        AddPets(mobDrops, "BABY_YETI", "ENDERMAN", "MAGMA_CUBE");
        AddPets(rareMobDrops, "GHOUL", "HOUND");
        AddPets(slugPrices, "SLUG", "SLUG_PET");
        AddPets(tarantulaPrices, "TARANTULA");
        AddPets(phoenixPrices, "PHOENIX");
        AddPets(milestone, "DOLPHIN", "ROCK");
        AddPets(darkAuction, "TURTLE", "PARROT", "JELLYFISH");
        AddPets(ammonite, "AMMONITE");
        AddPets(armadillo, "ARMADILLO");
        AddPets(bal, "BAL");
        AddPets(bee, "BEE");
        AddPets(blackCat, "BLACK_CAT");
        AddPets(enderDragon, "ENDER_DRAGON");
        AddPets(flyingFish, "FLYING_FISH");
        AddPets(goldenDragon, "GOLDEN_DRAGON");
        AddPets(golem, "GOLEM");
        AddPets(grandmaWolf, "GRANDMA_WOLF");
        AddPets(griffin, "GRIFFIN");
        AddPets(guardian, "GUARDIAN");
        AddPets(kuudra, "KUUDRA");
        AddPets(megalodon, "MEGALODON");
        AddPets(mithrilGolem, "MITHRIL_GOLEM");
        AddPets(rat, "RAT");
        AddPets(riftFerret, "RIFT_FERRET");
        AddPets(scatha, "SCATHA");
        AddPets(skeletonHorse, "SKELETON_HORSE");
        AddPets(spirit, "SPIRIT");
        AddPets(squid, "SQUID");

        return map;
    }

    private static IReadOnlyDictionary<CoreTier, double> CreatePriceMap(params (CoreTier rarity, double price)[] entries)
    {
        var dict = new Dictionary<CoreTier, double>();
        foreach (var (rarity, price) in entries)
        {
            if (price > 0)
                dict[rarity] = price;
        }
        return dict;
    }
}

public sealed record GeorgePetPriceSnapshot(IReadOnlyDictionary<PetKey, double> OffersByKey, IReadOnlyDictionary<string, PetOffer> OffersByTag)
{
    public static GeorgePetPriceSnapshot Empty { get; } = new GeorgePetPriceSnapshot(
        new Dictionary<PetKey, double>(),
        new Dictionary<string, PetOffer>(StringComparer.OrdinalIgnoreCase));

    public bool TryGetPrice(string itemTag, out double price)
    {
        if (OffersByTag.TryGetValue(itemTag, out var offer))
        {
            price = offer.Price;
            return true;
        }
        price = 0;
        return false;
    }

    public bool TryGetPrice(PetKey key, out double price)
    {
        return OffersByKey.TryGetValue(key, out price);
    }

    public IReadOnlyCollection<PetOffer> Offers => OffersByTag.Values.ToList();
}
