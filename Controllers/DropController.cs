using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers;

[ApiController]
[Route("[controller]")]
[ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
public class DropController : ControllerBase
{
    private readonly ILogger<DropController> _logger;
    private PriceDropService dropService;

    public DropController(ILogger<DropController> logger, PriceDropService dropService)
    {
        _logger = logger;
        this.dropService = dropService;
    }
    [HttpGet]
    [Route("all")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
    public IEnumerable<PriceDropService.DropStatistic> GetAllDrops()
    {
        return dropService.GetAllDrops();
    }

    [HttpGet]
    [Route("minions")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
    public IEnumerable<MinionRate> GetMinionDrops([FromServices] CraftingRecipeService craftingRecipeService)
    {
        var minions = craftingRecipeService.CraftAbleItems().Where(c => c.internalname.Contains("_GENERATOR_")).ToList();
        return minions.Select(m =>
        {
            var kind = m.internalname.Split("_GENERATOR_")[0];
            var seconds = m.lore?.First(l => l.StartsWith("§7Time Between Actions: §a")).Replace("§7Time Between Actions: §a", "").Replace("s", "") ?? "0";
            var maxStorage = m.lore?.First(l => l.StartsWith("§7Max Storage: §e")).Replace("§7Max Storage: §e", "").Replace(" coins", "") ?? "0";
            var tier = int.Parse(m.internalname.Split("_GENERATOR_")[1]);
            return (kind, tier, seconds, maxStorage);
        }).GroupBy(m => m.kind)
        .Select(g => new MinionRate()
        {
            Name = g.Key,
            Seconsds = g.OrderBy(m => m.tier).Select(m => double.Parse(m.seconds, CultureInfo.InvariantCulture)).ToArray(),
            Capacity = g.OrderBy(m => m.tier).Select(m => int.Parse(m.maxStorage)).ToArray()
        }).OrderBy(m => m.Name);
    }

    public class MinionRate
    {
        public string Name { get; set; }
        public double[] Seconsds { get; set; }
        public int[] Capacity { get; set; }
    }
}
