using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class KatController : ControllerBase
    {
        private readonly ILogger<KatController> _logger;
        private KatUpgradeService katService;

        public KatController(ILogger<KatController> logger, KatUpgradeService katService)
        {
            _logger = logger;
            this.katService = katService;
        }


        [HttpGet]
        [Route("profit")]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<KatUpgradeResult> GetProfitable()
        {
            return katService.Results.Where(c => c.Profit > 0).OrderByDescending(r => r.Profit / r.CoreData.Hours);
        }
        [HttpGet]
        [Route("all")]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<KatUpgradeResult> GetAll()
        {
            return katService.Results.OrderByDescending(c => c.Profit / c.CoreData.Hours);
        }
        [HttpGet]
        [Route("raw")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public Task<IEnumerable<KatUpgradeCost>> GetUpgradeData()
        {
            return katService.GetKatUpgradeCosts();
        }
    }
}