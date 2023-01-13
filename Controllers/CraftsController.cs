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
        [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<ProfitableCraft> Get()
        {
            return GetProfitable();
        }

        /// <summary>
        /// Profitable craft flips
        /// </summary>
        [HttpGet]
        [Route("profit")]
        [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<ProfitableCraft> GetProfitable()
        {
            return updaterService.Crafts.Values.Where(c =>
                (c.CraftCost < c.SellPrice * 0.95
                    || c.CraftCost < c.SellPrice * 0.99 && updaterService.BazaarItems.Contains(c.ItemId)
                ) && !c.Ingredients.Where(i => i.Cost <= 0).Any() && c.Type == null);
        }
        /// <summary>
        /// Returns craft prices of all know craftable items
        /// </summary>
        [HttpGet]
        [Route("all")]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<ProfitableCraft> GetAll()
        {
            return updaterService.Crafts.Select(e => e.Value).OrderByDescending(c => c.SellPrice - c.CraftCost);
        }
        [HttpGet]
        [Route("recipe/{itemTag}")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public Task<Recipe> GetRecipe(string itemTag)
        {
            return craftingRecipeService.GetRecipe(itemTag);
        }
        [HttpGet]
        [Route("ready")]
        [ResponseCache(Duration = 5, Location = ResponseCacheLocation.Any, NoStore = false)]
        public ActionResult GetReady()
        {
            if (updaterService.IteratedAll)
                return Ok();
            else
                return this.Problem("Not ready yet", statusCode: 503);
        }
    }
}