using GccLicenseWatchdog.Guardant;
using GccLicenseWatchdog.Recovery;
using GccLicenseWatchdog.State;

namespace GccLicenseWatchdog.Tests;

internal sealed class FakeWatchdogClock(DateTimeOffset? initialUtc = null) : IWatchdogClock
{
    public DateTimeOffset UtcNow { get; private set; } = initialUtc ?? DateTimeOffset.UnixEpoch;
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        UtcNow += delay;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTargetServiceController : ITargetServiceController
{
    public TargetServiceState State { get; set; } = TargetServiceState.Running;
    public int GetStateCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int StartCalls { get; private set; }
    public Func<FakeTargetServiceController, Task>? OnStopAsync { get; set; }
    public Func<FakeTargetServiceController, Task>? OnStartAsync { get; set; }
    public Func<FakeTargetServiceController, TargetServiceState>? OnGetState { get; set; }

    public Task<TargetServiceState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetStateCalls++;
        return Task.FromResult(OnGetState?.Invoke(this) ?? State);
    }

    public async Task RequestStopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCalls++;
        if (OnStopAsync is null)
        {
            State = TargetServiceState.Stopped;
            return;
        }

        await OnStopAsync(this);
    }

    public async Task RequestStartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCalls++;
        if (OnStartAsync is null)
        {
            State = TargetServiceState.Running;
            return;
        }

        await OnStartAsync(this);
    }
}

internal sealed class FakeGuardantClient : IGuardantClient
{
    public IReadOnlyList<FeatureInfo> Features { get; set; } = [];
    public IReadOnlyList<SessionInfo> Sessions { get; set; } = [];
    public Queue<bool> HealthResults { get; } = new();
    public Exception? FeaturesException { get; set; }
    public Exception? SessionsException { get; set; }
    public int FeatureCalls { get; private set; }
    public int SessionCalls { get; private set; }
    public int HealthCalls { get; private set; }

    public Task<IReadOnlyList<FeatureInfo>> GetFeaturesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FeatureCalls++;
        return FeaturesException is null
            ? Task.FromResult(Features)
            : Task.FromException<IReadOnlyList<FeatureInfo>>(FeaturesException);
    }

    public Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionCalls++;
        return SessionsException is null
            ? Task.FromResult(Sessions)
            : Task.FromException<IReadOnlyList<SessionInfo>>(SessionsException);
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthCalls++;
        return Task.FromResult(HealthResults.Count == 0 || HealthResults.Dequeue());
    }
}

internal sealed class FakeCooldownStore : IRestartCooldownStore
{
    public bool IsActive { get; set; }
    public int IsActiveCalls { get; private set; }
    public int MarkSucceededCalls { get; private set; }
    public DateTimeOffset? MarkedAtUtc { get; private set; }

    public Task<bool> IsActiveAsync(
        DateTimeOffset nowUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsActiveCalls++;
        return Task.FromResult(IsActive);
    }

    public Task MarkSucceededAsync(
        DateTimeOffset restartedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarkSucceededCalls++;
        MarkedAtUtc = restartedAtUtc;
        return Task.CompletedTask;
    }
}

internal sealed class FakeRecoveryManager : IGccRecoveryManager
{
    public RecoveryResult EnsureResult { get; set; } = new(
        true,
        false,
        RecoveryOutcome.AlreadyRunning,
        "running");

    public RecoveryResult RestartResult { get; set; } = new(
        true,
        true,
        RecoveryOutcome.Restarted,
        "restarted");

    public int EnsureCalls { get; private set; }
    public int RestartCalls { get; private set; }

    public Task<RecoveryResult> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCalls++;
        return Task.FromResult(EnsureResult);
    }

    public Task<RecoveryResult> RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestartCalls++;
        return Task.FromResult(RestartResult);
    }
}
