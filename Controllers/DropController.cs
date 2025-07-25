using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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
    public IEnumerable<MinionRate> GetMinionDrops()
    {
        var minions = GetMinions().ToList();
        return minions.Select(m =>
        {
            var kind = m.internalname.Split("_GENERATOR_")[0];
            var seconds = m.lore?.FirstOrDefault(l => l.StartsWith("§7Time Between Actions: §a"))?.Replace("§7Time Between Actions: §a", "").Replace("s", "") ?? "0";
            if(seconds == "0")
                Console.WriteLine($"No time found for {m.internalname} in {JsonConvert.SerializeObject(m)}");
            var maxStorage = m.lore?.FirstOrDefault(l => l.StartsWith("§7Max Storage: §e"))?.Replace("§7Max Storage: §e", "").Replace(" coins", "") ?? "0";
            var tier = int.Parse(m.internalname.Split("_GENERATOR_")[1]);
            kind = m.displayname.Substring(2, m.displayname.Length - 2 - m.displayname.Split(' ').Last().Length).Trim();
            return (kind, tier, seconds, maxStorage);
        }).GroupBy(m => m.kind)
        .Select(g => new MinionRate()
        {
            Name = g.Key,
            Seconsds = g.OrderBy(m => m.tier).Select(m => double.Parse(m.seconds, CultureInfo.InvariantCulture)).ToArray(),
            Capacity = g.OrderBy(m => m.tier).Select(m => int.Parse(m.maxStorage)).ToArray()
        }).OrderBy(m => m.Name);
    }

    private IEnumerable<Models.ItemData> GetMinions()
    {
        foreach (var itemPath in Directory.EnumerateFiles("itemData/items"))
        {
            if(!itemPath.Contains("_GENERATOR_"))
                continue;
            yield return JsonConvert.DeserializeObject<Models.ItemData>(System.IO.File.ReadAllText(itemPath));
        }
    }


    public class MinionRate
    {
        public string Name { get; set; }
        public double[] Seconsds { get; set; }
        public int[] Capacity { get; set; }
    }
}
