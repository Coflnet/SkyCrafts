using System.Collections.Generic;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Controllers;
[ApiController]
[Route("[controller]")]
[ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
public class DropController : ControllerBase
{
    private readonly ILogger<DropController> _logger;
    private PriceDropService dropService;

    public DropController(ILogger<DropController> logger, PriceDropService dropService)
    {
        _logger = logger;
        this.dropService = dropService;
    }
    [HttpGet]
    [Route("all")]
    [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
    public IEnumerable<PriceDropService.DropStatistic> GetAllDrops()
    {
        return dropService.GetAllDrops();
    }
}
