using GccLicenseWatchdog.Detection;
using GccLicenseWatchdog.Guardant;
using GccLicenseWatchdog.Recovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog.Tests;

public sealed class WatchdogEngineTests
{
    private static readonly FeatureKey FirstKey = new(100, 200, 1, 9, 2);
    private static readonly FeatureKey SecondKey = new(100, 200, 1, 9, 4);

    [Fact]
    public async Task FreeResourceDoesNotRequestSessions()
    {
        var api = new FakeGuardantClient { Features = [Feature(FirstKey, floating: 1)] };
        var recovery = new FakeRecoveryManager();
        var engine = CreateEngine(api, recovery: recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.Healthy, result.Outcome);
        Assert.Equal(1, recovery.EnsureCalls);
        Assert.Equal(0, api.SessionCalls);
        Assert.Equal(0, recovery.RestartCalls);
    }

    [Fact]
    public async Task UniqueUsersAtExhaustionAreDiagnosticOnly()
    {
        var api = new FakeGuardantClient
        {
            Features = [Feature(FirstKey, floating: 0)],
            Sessions = [Session(1, FirstKey, "42"), Session(2, FirstKey, "43")]
        };
        var recovery = new FakeRecoveryManager();
        var engine = CreateEngine(api, recovery: recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.ExhaustedWithUniqueUsers, result.Outcome);
        Assert.Equal(1, api.SessionCalls);
        Assert.Equal(0, recovery.RestartCalls);
    }

    [Fact]
    public async Task DuplicateUserRestartsImmediatelyAndMarksCooldown()
    {
        var api = DuplicateIncidentApi(FirstKey);
        var recovery = new FakeRecoveryManager();
        var cooldown = new FakeCooldownStore();
        var clock = new FakeWatchdogClock(DateTimeOffset.Parse("2026-08-25T10:00:00Z"));
        var engine = CreateEngine(api, cooldown, recovery, clock);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.Restarted, result.Outcome);
        Assert.Equal(1, recovery.RestartCalls);
        Assert.Equal(1, cooldown.MarkSucceededCalls);
        Assert.Equal(clock.UtcNow, cooldown.MarkedAtUtc);
    }

    [Fact]
    public async Task ActiveCooldownPreventsRestart()
    {
        var api = DuplicateIncidentApi(FirstKey);
        var recovery = new FakeRecoveryManager();
        var cooldown = new FakeCooldownStore { IsActive = true };
        var engine = CreateEngine(api, cooldown, recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.CooldownActive, result.Outcome);
        Assert.Equal(0, recovery.RestartCalls);
        Assert.Equal(0, cooldown.MarkSucceededCalls);
    }

    [Fact]
    public async Task MultipleCandidatesCauseOneCombinedRestart()
    {
        var api = new FakeGuardantClient
        {
            Features = [Feature(FirstKey, 0), Feature(SecondKey, 0)],
            Sessions =
            [
                Session(1, FirstKey, "42"),
                Session(2, FirstKey, "42"),
                Session(3, SecondKey, "84"),
                Session(4, SecondKey, "84")
            ]
        };
        var recovery = new FakeRecoveryManager();
        var engine = CreateEngine(api, recovery: recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, result.Report!.RestartCandidates.Count);
        Assert.Equal(1, recovery.RestartCalls);
    }

    [Fact]
    public async Task SessionReadFailureDoesNotRestart()
    {
        var api = new FakeGuardantClient
        {
            Features = [Feature(FirstKey, 0)],
            SessionsException = new HttpRequestException("partial response")
        };
        var recovery = new FakeRecoveryManager();
        var engine = CreateEngine(api, recovery: recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.SessionReadFailed, result.Outcome);
        Assert.Equal(0, recovery.RestartCalls);
    }

    [Fact]
    public async Task FailedRecoveryDoesNotMarkCooldown()
    {
        var recovery = new FakeRecoveryManager
        {
            RestartResult = new RecoveryResult(false, true, RecoveryOutcome.StartFailed, "failed")
        };
        var cooldown = new FakeCooldownStore();
        var engine = CreateEngine(DuplicateIncidentApi(FirstKey), cooldown, recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.RecoveryFailed, result.Outcome);
        Assert.Equal(0, cooldown.MarkSucceededCalls);
    }

    [Fact]
    public async Task UnavailableTargetSkipsLicenseApi()
    {
        var api = new FakeGuardantClient { Features = [Feature(FirstKey, 0)] };
        var recovery = new FakeRecoveryManager
        {
            EnsureResult = new RecoveryResult(false, false, RecoveryOutcome.StartFailed, "failed")
        };
        var engine = CreateEngine(api, recovery: recovery);

        var result = await engine.RunCycleAsync(CancellationToken.None);

        Assert.Equal(WatchdogCycleOutcome.TargetUnavailable, result.Outcome);
        Assert.Equal(0, api.FeatureCalls);
    }

    private static WatchdogEngine CreateEngine(
        FakeGuardantClient api,
        FakeCooldownStore? cooldown = null,
        FakeRecoveryManager? recovery = null,
        FakeWatchdogClock? clock = null) => new(
            api,
            new LicenseIncidentDetector(),
            cooldown ?? new FakeCooldownStore(),
            recovery ?? new FakeRecoveryManager(),
            clock ?? new FakeWatchdogClock(),
            Options.Create(new WatchdogOptions()),
            NullLogger<WatchdogEngine>.Instance);

    private static FakeGuardantClient DuplicateIncidentApi(FeatureKey key) => new()
    {
        Features = [Feature(key, 0)],
        Sessions = [Session(1, key, "42"), Session(2, key, "42")]
    };

    private static FeatureInfo Feature(FeatureKey key, int floating) => new(
        key,
        "ДАЛИОН",
        $"Feature {key.FeatureNumber}",
        3,
        false,
        floating,
        11,
        11 - floating);

    private static SessionInfo Session(long sessionId, FeatureKey key, string userId) => new(
        sessionId,
        key,
        userId,
        $"User {userId}",
        DateTimeOffset.UnixEpoch,
        "rphost",
        100);
}
