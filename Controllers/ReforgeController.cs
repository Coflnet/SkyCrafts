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
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
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
        public async Task<Dictionary<string,string>> GetReforgeStones()
        {
            var reforges = await reforgeService.GetReforges();
            return reforges.ToDictionary(r => r.Value.ReforgeName, r => r.Key);
        }
    }
}