using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Coflnet.Sky.Crafts.Models;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CraftsController : ControllerBase
    {

        private readonly ILogger<CraftsController> _logger;
        private UpdaterService updaterService;

        public CraftsController(ILogger<CraftsController> logger, UpdaterService updaterService)
        {
            _logger = logger;
            this.updaterService = updaterService;
        }

        [HttpGet]
        public IEnumerable<ProfitableCraft> Get()
        {
            return updaterService.Crafts.Values.Where(c=>c.CraftCost<c.SellPrice * 0.95);
        }
    }
}