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
    public class KatController : ControllerBase
    {

        private readonly ILogger<KatController> _logger;
        private KatUpgradeService katService;

        public  KatController(ILogger<KatController> logger, KatUpgradeService katService)
        {
            _logger = logger;
            this.katService = katService;
        }


        [HttpGet]
        [Route("profit")]
        public IEnumerable<KatUpgradeResult> GetProfitable()
        {
            return katService.Results.Where(c => c.Profit > 0).OrderByDescending(r=>r.Profit / r.CoreData.Hours);
        }
        [HttpGet]
        [Route("all")]
        public IEnumerable<KatUpgradeResult> GetAll()
        {
            return katService.Results.OrderByDescending(c=>c.Profit / c.CoreData.Hours);
        }
        [HttpGet]
        [Route("raw")]
        public Task<IEnumerable<KatUpgradeCost>> GetUpgradeData()
        {
            return katService.GetKatUpgradeCosts();
        }
    }
}