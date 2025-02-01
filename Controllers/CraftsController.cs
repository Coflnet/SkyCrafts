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
            return updaterService.Crafts.Values.Where(e => e != null).Where(c =>
                (c.CraftCost < c.SellPrice * 0.98 -1
                    || c.CraftCost < c.SellPrice * 0.99 && updaterService.BazaarItems.Contains(c.ItemId)
                ) && !c.Ingredients.Where(i => i.Cost <= 0).Any() && c.Type == null && c.Volume > 2);
        }
        /// <summary>
        /// Returns craft prices of all know craftable items
        /// </summary>
        [HttpGet]
        [Route("all")]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public IEnumerable<ProfitableCraft> GetAll()
        {
            return updaterService.Crafts.Where(e => e.Value != null).Select(e => e.Value).OrderByDescending(c => c.SellPrice - c.CraftCost);
        }
        /// <summary>
        /// Get the recipe for a specific item
        /// </summary>
        [HttpGet]
        [Route("recipe/{itemTag}")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public Task<Recipe> GetRecipe(string itemTag)
        {
            return craftingRecipeService.GetRecipe(itemTag);
        }
        /// <summary>
        /// Returns true if all items been calculated once.
        /// Necessary for determining if batch requests return valid responses
        /// </summary>
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

        [HttpGet]
        [Route("neu/{itemTag}")]
        public async Task<ItemData> GetNeu(string itemTag)
        {
            return await craftingRecipeService.GetItemData(itemTag);
        }
    }
}