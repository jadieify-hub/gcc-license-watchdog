using GccLicenseWatchdog.Guardant;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog.Recovery;

public enum RecoveryOutcome
{
    AlreadyRunning,
    Started,
    Restarted,
    ServiceMissing,
    StopTimedOut,
    StartFailed,
    StartRetryDeferred,
    ApiUnavailable
}

public sealed record RecoveryResult(
    bool Success,
    bool RestartPerformed,
    RecoveryOutcome Outcome,
    string Message);

public interface IGccRecoveryManager
{
    Task<RecoveryResult> EnsureAvailableAsync(CancellationToken cancellationToken);
    Task<RecoveryResult> RestartAsync(CancellationToken cancellationToken);
}

public sealed class GccRecoveryManager(
    ITargetServiceController serviceController,
    IGuardantClient guardantClient,
    IOptions<WatchdogOptions> options,
    IWatchdogClock clock,
    ILogger<GccRecoveryManager> logger) : IGccRecoveryManager
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly WatchdogOptions _options = options.Value;
    private DateTimeOffset? _nextStartAttemptUtc;

    public async Task<RecoveryResult> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        await _recoveryGate.WaitAsync(cancellationToken);
        try
        {
            var state = await serviceController.GetStateAsync(cancellationToken);
            if (state == TargetServiceState.Missing)
            {
                return Failure(false, RecoveryOutcome.ServiceMissing, "Guardant Control Center service is missing.");
            }

            if (state == TargetServiceState.Running)
            {
                _nextStartAttemptUtc = null;
                return Success(false, RecoveryOutcome.AlreadyRunning, "Guardant Control Center is running.");
            }

            if (state == TargetServiceState.StartPending)
            {
                if (!await WaitForStateAsync(
                    TargetServiceState.Running,
                    TimeSpan.FromSeconds(_options.StartTimeoutSeconds),
                    cancellationToken))
                {
                    return Failure(false, RecoveryOutcome.StartFailed, "Guardant Control Center start timed out.");
                }

                if (await WaitForApiAsync(cancellationToken))
                {
                    _nextStartAttemptUtc = null;
                    return Success(false, RecoveryOutcome.Started, "Guardant Control Center and its API are ready.");
                }

                return Failure(false, RecoveryOutcome.ApiUnavailable, "Guardant Control Center API is unavailable.");
            }

            if (state == TargetServiceState.StopPending &&
                !await WaitForStateAsync(
                    TargetServiceState.Stopped,
                    TimeSpan.FromSeconds(_options.StopTimeoutSeconds),
                    cancellationToken))
            {
                return Failure(false, RecoveryOutcome.StopTimedOut, "Guardant Control Center did not finish stopping.");
            }

            if (_nextStartAttemptUtc > clock.UtcNow)
            {
                return Failure(
                    false,
                    RecoveryOutcome.StartRetryDeferred,
                    $"The next Guardant Control Center start attempt is deferred until {_nextStartAttemptUtc:O}.");
            }

            return await StartAndVerifyAsync(restartPerformed: false, cancellationToken);
        }
        finally
        {
            _recoveryGate.Release();
        }
    }

    public async Task<RecoveryResult> RestartAsync(CancellationToken cancellationToken)
    {
        await _recoveryGate.WaitAsync(cancellationToken);
        try
        {
            var state = await serviceController.GetStateAsync(cancellationToken);
            if (state == TargetServiceState.Missing)
            {
                return Failure(false, RecoveryOutcome.ServiceMissing, "Guardant Control Center service is missing.");
            }

            if (state == TargetServiceState.StopPending)
            {
                if (!await WaitForStateAsync(
                    TargetServiceState.Stopped,
                    TimeSpan.FromSeconds(_options.StopTimeoutSeconds),
                    cancellationToken))
                {
                    return Failure(true, RecoveryOutcome.StopTimedOut, "Guardant Control Center stop timed out.");
                }
            }
            else if (state != TargetServiceState.Stopped)
            {
                logger.LogWarning("Stopping Guardant Control Center for license recovery.");
                await serviceController.RequestStopAsync(cancellationToken);
                if (!await WaitForStateAsync(
                    TargetServiceState.Stopped,
                    TimeSpan.FromSeconds(_options.StopTimeoutSeconds),
                    cancellationToken))
                {
                    return Failure(true, RecoveryOutcome.StopTimedOut, "Guardant Control Center stop timed out.");
                }
            }

            return await StartAndVerifyAsync(restartPerformed: true, cancellationToken);
        }
        finally
        {
            _recoveryGate.Release();
        }
    }

    private async Task<RecoveryResult> StartAndVerifyAsync(
        bool restartPerformed,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await serviceController.RequestStartAsync(cancellationToken);
                if (await WaitForStateAsync(
                    TargetServiceState.Running,
                    TimeSpan.FromSeconds(_options.StartTimeoutSeconds),
                    cancellationToken))
                {
                    if (await WaitForApiAsync(cancellationToken))
                    {
                        _nextStartAttemptUtc = null;
                        var outcome = restartPerformed ? RecoveryOutcome.Restarted : RecoveryOutcome.Started;
                        return Success(restartPerformed, outcome, "Guardant Control Center and its API are ready.");
                    }

                    return Failure(
                        restartPerformed,
                        RecoveryOutcome.ApiUnavailable,
                        "Guardant Control Center is running but its API is unavailable.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to start Guardant Control Center on attempt {Attempt}.", attempt);
            }

            if (attempt < 3)
            {
                await clock.DelayAsync(StartRetryDelay, cancellationToken);
            }
        }

        _nextStartAttemptUtc = clock.UtcNow + TimeSpan.FromSeconds(60);
        return Failure(
            restartPerformed,
            RecoveryOutcome.StartFailed,
            "Guardant Control Center did not reach Running after three attempts.");
    }

    private async Task<bool> WaitForStateAsync(
        TargetServiceState expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = clock.UtcNow + timeout;
        while (true)
        {
            var state = await serviceController.GetStateAsync(cancellationToken);
            if (state == expectedState)
            {
                return true;
            }

            if (state == TargetServiceState.Missing || clock.UtcNow >= deadline)
            {
                return false;
            }

            await clock.DelayAsync(StatusPollInterval, cancellationToken);
        }
    }

    private async Task<bool> WaitForApiAsync(CancellationToken cancellationToken)
    {
        var deadline = clock.UtcNow + TimeSpan.FromSeconds(_options.ApiReadyTimeoutSeconds);
        while (true)
        {
            if (await guardantClient.IsHealthyAsync(cancellationToken))
            {
                return true;
            }

            if (clock.UtcNow >= deadline)
            {
                return false;
            }

            await clock.DelayAsync(StatusPollInterval, cancellationToken);
        }
    }

    private static RecoveryResult Success(
        bool restartPerformed,
        RecoveryOutcome outcome,
        string message) => new(true, restartPerformed, outcome, message);

    private static RecoveryResult Failure(
        bool restartPerformed,
        RecoveryOutcome outcome,
        string message) => new(false, restartPerformed, outcome, message);
}
