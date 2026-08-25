using GccLicenseWatchdog.Recovery;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog;

public sealed class WatchdogWorker(
    IWatchdogEngine engine,
    IWatchdogClock clock,
    IOptions<WatchdogOptions> options,
    ILogger<WatchdogWorker> logger) : BackgroundService
{
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GCC License Watchdog started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await engine.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected watchdog cycle failure.");
            }

            try
            {
                await clock.DelayAsync(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("GCC License Watchdog stopped.");
    }
}
