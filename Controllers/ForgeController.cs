using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers;
[ApiController]
[Route("[controller]")]
[ResponseCache(Duration = 180, Location = ResponseCacheLocation.Any, NoStore = false)]
public class ForgeController : ControllerBase
{
    private readonly ILogger<ForgeController> _logger;
    private ForgeCraftService forgeService;

    public ForgeController(ILogger<ForgeController> logger, ForgeCraftService forgeService)
    {
        _logger = logger;
        this.forgeService = forgeService;
    }

    [HttpGet]
    [Route("all")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public IEnumerable<ForgeFlip> GetAllForge()
    {
        return forgeService.FlipList.OrderByDescending(f => f.ProfitPerHour);
    }
}
