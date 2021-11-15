using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
        private CraftingRecipeService craftingRecipeService;

        public CraftsController(ILogger<CraftsController> logger, UpdaterService updaterService, CraftingRecipeService craftingRecipeService)
        {
            _logger = logger;
            this.updaterService = updaterService;
            this.craftingRecipeService = craftingRecipeService;
        }

        [HttpGet]
        public IEnumerable<ProfitableCraft> Get()
        {
            return GetProfitable();
        }


        [HttpGet]
        [Route("profit")]
        public IEnumerable<ProfitableCraft> GetProfitable()
        {
            return updaterService.Crafts.Values.Where(c => c.CraftCost < c.SellPrice * 0.95 && !c.Ingredients.Where(i => i.Cost <= 0).Any());
        }
        [HttpGet]
        [Route("recipe/{itemTag}")]
        public Task<Dictionary<string, string>> GetRecipe(string itemTag)
        {
            return craftingRecipeService.GetRecipe(itemTag);
        }
    }
}