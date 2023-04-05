using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class ReforgeController
    {
        private readonly ILogger<ReforgeController> _logger;
        private IReforgeService reforgeService;

        public ReforgeController(ILogger<ReforgeController> logger, IReforgeService reforgeService)
        {
            _logger = logger;
            this.reforgeService = reforgeService;
        }

        [HttpGet]
        [Route("stones")]
        public async Task<Dictionary<string,Response>> GetReforgeStones()
        {
            var reforges = await reforgeService.GetReforges();
            return reforges.ToDictionary(r => r.Value.ReforgeName, r => new Response(r.Key, r.Value.ReforgeCosts.GetValueOrDefault("LEGENDARY")));
        }

        public class Response
        {
            public string Tag { get; set; }
            public int LegendaryCost { get; set; }

            public Response(string tag, int legendaryCost)
            {
                Tag = tag;
                LegendaryCost = legendaryCost;
            }
        }
    }
}