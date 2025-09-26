using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers
{
    [ApiController]
    [Route("npc")]
    public class NpcController : ControllerBase
    {
        private readonly NpcSellService npcSellService;
        private readonly ILogger<NpcController> logger;

        public NpcController(NpcSellService npcSellService, ILogger<NpcController> logger)
        {
            this.npcSellService = npcSellService;
            this.logger = logger;
        }

        [HttpGet("flips")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<NpcFlip>> GetNpcFlips([FromQuery] bool forceRefresh = false)
        {
            var flips = await npcSellService.GetNpcFlipOpportunities(forceRefresh);
            logger.LogInformation("Returning {count} NPC flips", flips.Count);
            return flips;
        }
    }
}
