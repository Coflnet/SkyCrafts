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
    private readonly GeorgePetOfferService georgePetOfferService;
    private readonly GeorgeFlipService georgeFlipService;
        private readonly ILogger<NpcController> logger;

        public NpcController(NpcSellService npcSellService, GeorgePetOfferService georgePetOfferService, GeorgeFlipService georgeFlipService, ILogger<NpcController> logger)
        {
            this.npcSellService = npcSellService;
            this.georgePetOfferService = georgePetOfferService;
            this.georgeFlipService = georgeFlipService;
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

        [HttpGet("george-offers")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IReadOnlyDictionary<string, PetOffer>> GetGeorgeOffers([FromQuery] bool forceRefresh = false)
        {
            var snapshot = await georgePetOfferService.GetSnapshotAsync(forceRefresh);
            logger.LogInformation("Returning {count} George pet offers", snapshot.OffersByTag.Count);
            return snapshot.OffersByTag;
        }

        [HttpGet("george-flips")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<GeorgeFlipResult>> GetGeorgeFlips([FromQuery] bool forceRefresh = false)
        {
            await georgeFlipService.Update(forceRefresh);
            logger.LogInformation("Returning {count} George flips", georgeFlipService.Results.Count);
            return georgeFlipService.Results;
        }
    }
}
