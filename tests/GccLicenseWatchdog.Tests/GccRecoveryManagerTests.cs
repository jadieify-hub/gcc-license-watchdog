using GccLicenseWatchdog.Recovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog.Tests;

public sealed class GccRecoveryManagerTests
{
    [Fact]
    public async Task RestartStopsWaitsStartsAndChecksApi()
    {
        var service = new FakeTargetServiceController();
        var api = new FakeGuardantClient();
        var manager = CreateManager(service, api);

        var result = await manager.RestartAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.RestartPerformed);
        Assert.Equal(1, service.StopCalls);
        Assert.Equal(1, service.StartCalls);
        Assert.Equal(1, api.HealthCalls);
        Assert.Equal(TargetServiceState.Running, service.State);
    }

    [Fact]
    public async Task CancellationBeforeRecoveryDoesNotStopGcc()
    {
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var service = new FakeTargetServiceController();
        var manager = CreateManager(service, new FakeGuardantClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.RestartAsync(stopping.Token));

        Assert.Equal(TargetServiceState.Running, service.State);
        Assert.Equal(0, service.StopCalls);
    }

    [Fact]
    public async Task RestartDoesNotStartWhenStopTimesOut()
    {
        var service = new FakeTargetServiceController
        {
            OnStopAsync = controller =>
            {
                controller.State = TargetServiceState.StopPending;
                return Task.CompletedTask;
            }
        };
        var manager = CreateManager(service, new FakeGuardantClient());

        var result = await manager.RestartAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(RecoveryOutcome.StopTimedOut, result.Outcome);
        Assert.Equal(0, service.StartCalls);
    }

    [Fact]
    public async Task RestartWaitsForExistingStopPendingWithoutSendingAnotherStop()
    {
        var service = new FakeTargetServiceController { State = TargetServiceState.StopPending };
        service.OnGetState = controller => controller.StartCalls > 0
            ? TargetServiceState.Running
            : controller.GetStateCalls >= 3
                ? TargetServiceState.Stopped
                : TargetServiceState.StopPending;
        var manager = CreateManager(service, new FakeGuardantClient());

        var result = await manager.RestartAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, service.StopCalls);
        Assert.Equal(1, service.StartCalls);
    }

    [Fact]
    public async Task RestartRetriesStartThreeTimes()
    {
        var service = new FakeTargetServiceController
        {
            OnStartAsync = controller =>
            {
                controller.State = controller.StartCalls < 3
                    ? TargetServiceState.Stopped
                    : TargetServiceState.Running;
                return Task.CompletedTask;
            }
        };
        var manager = CreateManager(service, new FakeGuardantClient());

        var result = await manager.RestartAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, service.StartCalls);
    }

    [Fact]
    public async Task RestartIsNotSuccessfulUntilApiIsHealthy()
    {
        var api = new FakeGuardantClient();
        api.HealthResults.Enqueue(false);
        api.HealthResults.Enqueue(false);
        api.HealthResults.Enqueue(false);
        var manager = CreateManager(new FakeTargetServiceController(), api);

        var result = await manager.RestartAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(RecoveryOutcome.ApiUnavailable, result.Outcome);
        Assert.True(api.HealthCalls >= 2);
    }

    [Fact]
    public async Task EnsureAvailableStartsStoppedServiceWithoutIssuingStop()
    {
        var service = new FakeTargetServiceController { State = TargetServiceState.Stopped };
        var manager = CreateManager(service, new FakeGuardantClient());

        var result = await manager.EnsureAvailableAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.RestartPerformed);
        Assert.Equal(0, service.StopCalls);
        Assert.Equal(1, service.StartCalls);
    }

    [Fact]
    public async Task EnsureAvailableDoesNothingWhenRunningApiTemporarilyFails()
    {
        var service = new FakeTargetServiceController { State = TargetServiceState.Running };
        var api = new FakeGuardantClient();
        api.HealthResults.Enqueue(false);
        var manager = CreateManager(service, api);

        var result = await manager.EnsureAvailableAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(RecoveryOutcome.AlreadyRunning, result.Outcome);
        Assert.Equal(0, service.StopCalls);
        Assert.Equal(0, service.StartCalls);
        Assert.Equal(0, api.HealthCalls);
    }

    [Fact]
    public async Task EnsureAvailableWaitsForStartPendingWithoutSendingAnotherStart()
    {
        var service = new FakeTargetServiceController { State = TargetServiceState.StartPending };
        service.OnGetState = controller => controller.GetStateCalls >= 3
            ? TargetServiceState.Running
            : TargetServiceState.StartPending;
        var manager = CreateManager(service, new FakeGuardantClient());

        var result = await manager.EnsureAvailableAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, service.StartCalls);
    }

    [Fact]
    public async Task EnsureAvailableDefersNewAttemptsForSixtySecondsAfterThreeFailures()
    {
        var service = new FakeTargetServiceController
        {
            State = TargetServiceState.Stopped,
            OnStartAsync = controller =>
            {
                controller.State = TargetServiceState.Stopped;
                return Task.CompletedTask;
            }
        };
        var clock = new FakeWatchdogClock();
        var manager = CreateManager(service, new FakeGuardantClient(), clock);

        var first = await manager.EnsureAvailableAsync(CancellationToken.None);
        var attemptsAfterFailure = service.StartCalls;
        var second = await manager.EnsureAvailableAsync(CancellationToken.None);

        Assert.False(first.Success);
        Assert.Equal(3, attemptsAfterFailure);
        Assert.Equal(RecoveryOutcome.StartRetryDeferred, second.Outcome);
        Assert.Equal(attemptsAfterFailure, service.StartCalls);

        await clock.DelayAsync(TimeSpan.FromSeconds(60), CancellationToken.None);
        _ = await manager.EnsureAvailableAsync(CancellationToken.None);
        Assert.True(service.StartCalls > attemptsAfterFailure);
    }

    [Fact]
    public async Task ConcurrentRecoveryIsSerialized()
    {
        var firstStopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeTargetServiceController
        {
            OnStopAsync = async controller =>
            {
                if (controller.StopCalls == 1)
                {
                    firstStopEntered.SetResult();
                    await allowFirstStop.Task;
                }

                controller.State = TargetServiceState.Stopped;
            }
        };
        var manager = CreateManager(service, new FakeGuardantClient());

        var first = manager.RestartAsync(CancellationToken.None);
        await firstStopEntered.Task;
        var second = manager.RestartAsync(CancellationToken.None);
        await Task.Yield();

        Assert.Equal(1, service.StopCalls);
        allowFirstStop.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, service.StopCalls);
    }

    private static GccRecoveryManager CreateManager(
        ITargetServiceController service,
        FakeGuardantClient api,
        FakeWatchdogClock? clock = null)
    {
        var options = Options.Create(new WatchdogOptions
        {
            StopTimeoutSeconds = 2,
            StartTimeoutSeconds = 2,
            ApiReadyTimeoutSeconds = 2
        });
        return new GccRecoveryManager(
            service,
            api,
            options,
            clock ?? new FakeWatchdogClock(),
            NullLogger<GccRecoveryManager>.Instance);
    }
}
