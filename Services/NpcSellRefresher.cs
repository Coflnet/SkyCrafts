using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Crafts.Services;

/// <summary>
/// Background service that refreshes NPC sell prices and flips every hour.
/// Calls the NpcSellService on startup and then on an hourly interval.
/// </summary>
public class NpcSellRefresher : BackgroundService
{
    private readonly NpcSellService npcSellService;
    private readonly ILogger<NpcSellRefresher> logger;
    private readonly TimeSpan refreshInterval = TimeSpan.FromHours(1);

    public NpcSellRefresher(NpcSellService npcSellService, ILogger<NpcSellRefresher> logger)
    {
        this.npcSellService = npcSellService ?? throw new ArgumentNullException(nameof(npcSellService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initial refresh on startup (force)
            logger.LogInformation("NpcSellRefresher: performing initial refresh");
            await SafeRefresh(true, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NpcSellRefresher initial refresh failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(refreshInterval, stoppingToken);
                logger.LogInformation("NpcSellRefresher: performing scheduled refresh");
                await SafeRefresh(false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown requested
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NpcSellRefresher scheduled refresh failed");
            }
        }
    }

    private async Task SafeRefresh(bool force, CancellationToken ct)
    {
        try
        {
            // Refresh both NPC sell prices and flips. Force parameter passed to ensure caches update on startup.
            await npcSellService.GetNpcSellPrices(force);
            await npcSellService.GetNpcFlipOpportunities(force);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while refreshing NpcSellService caches");
        }
    }
}
