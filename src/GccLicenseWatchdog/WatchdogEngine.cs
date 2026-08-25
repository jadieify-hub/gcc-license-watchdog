using GccLicenseWatchdog.Detection;
using GccLicenseWatchdog.Guardant;
using GccLicenseWatchdog.Recovery;
using GccLicenseWatchdog.State;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog;

public enum WatchdogCycleOutcome
{
    Healthy,
    TargetUnavailable,
    FeatureReadFailed,
    SessionReadFailed,
    ExhaustedWithUniqueUsers,
    CooldownActive,
    Restarted,
    RecoveryFailed
}

public sealed record WatchdogCycleResult(
    WatchdogCycleOutcome Outcome,
    DetectionReport? Report = null,
    RecoveryResult? Recovery = null);

public interface IWatchdogEngine
{
    Task<WatchdogCycleResult> RunCycleAsync(CancellationToken cancellationToken);
}

public sealed class WatchdogEngine(
    IGuardantClient guardantClient,
    LicenseIncidentDetector detector,
    IRestartCooldownStore cooldownStore,
    IGccRecoveryManager recoveryManager,
    IWatchdogClock clock,
    IOptions<WatchdogOptions> options,
    ILogger<WatchdogEngine> logger) : IWatchdogEngine
{
    private readonly WatchdogOptions _options = options.Value;
    private DateTimeOffset? _lastUniqueExhaustionWarningUtc;

    public async Task<WatchdogCycleResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        var availability = await recoveryManager.EnsureAvailableAsync(cancellationToken);
        if (!availability.Success)
        {
            logger.LogCritical(
                "Guardant Control Center is unavailable: {Outcome}. {Message}",
                availability.Outcome,
                availability.Message);
            return new WatchdogCycleResult(WatchdogCycleOutcome.TargetUnavailable, Recovery: availability);
        }

        IReadOnlyList<FeatureInfo> features;
        try
        {
            features = await guardantClient.GetFeaturesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read Guardant license features.");
            return new WatchdogCycleResult(WatchdogCycleOutcome.FeatureReadFailed);
        }

        var preliminaryReport = detector.Evaluate(features, []);
        if (preliminaryReport.ExhaustedFeatures.Count == 0)
        {
            return new WatchdogCycleResult(WatchdogCycleOutcome.Healthy, preliminaryReport);
        }

        IReadOnlyList<SessionInfo> sessions;
        try
        {
            sessions = await guardantClient.GetAllSessionsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to read the complete Guardant session list; recovery decision is suppressed.");
            return new WatchdogCycleResult(WatchdogCycleOutcome.SessionReadFailed, preliminaryReport);
        }

        var report = detector.Evaluate(features, sessions);
        if (report.RestartCandidates.Count == 0)
        {
            LogUniqueExhaustionIfDue(report);
            return new WatchdogCycleResult(WatchdogCycleOutcome.ExhaustedWithUniqueUsers, report);
        }

        var cooldown = TimeSpan.FromMinutes(_options.CooldownMinutes);
        if (await cooldownStore.IsActiveAsync(clock.UtcNow, cooldown, cancellationToken))
        {
            logger.LogWarning(
                "Guardant recovery is suppressed by cooldown for {CandidateCount} exhausted component(s).",
                report.RestartCandidates.Count);
            return new WatchdogCycleResult(WatchdogCycleOutcome.CooldownActive, report);
        }

        LogIncident(report);
        var recovery = await recoveryManager.RestartAsync(cancellationToken);
        if (!recovery.Success)
        {
            logger.LogCritical(
                "Guardant recovery failed: {Outcome}. {Message}",
                recovery.Outcome,
                recovery.Message);
            return new WatchdogCycleResult(WatchdogCycleOutcome.RecoveryFailed, report, recovery);
        }

        await cooldownStore.MarkSucceededAsync(clock.UtcNow, cancellationToken);
        logger.LogInformation("Guardant Control Center recovered successfully.");
        return new WatchdogCycleResult(WatchdogCycleOutcome.Restarted, report, recovery);
    }

    private void LogUniqueExhaustionIfDue(DetectionReport report)
    {
        var interval = TimeSpan.FromMinutes(_options.DiagnosticLogIntervalMinutes);
        if (_lastUniqueExhaustionWarningUtc.HasValue &&
            clock.UtcNow - _lastUniqueExhaustionWarningUtc.Value < interval)
        {
            return;
        }

        _lastUniqueExhaustionWarningUtc = clock.UtcNow;
        logger.LogWarning(
            "Guardant license resource is exhausted for {FeatureCount} component(s), but all identified users are unique; no restart is performed.",
            report.ExhaustedFeatures.Count);
    }

    private void LogIncident(DetectionReport report)
    {
        foreach (var candidate in report.RestartCandidates)
        {
            logger.LogWarning(
                "Guardant recovery triggered for {Product}/{Feature} ({Key}): sessions={SessionCount}, uniqueUsers={UniqueUsers}, duplicates={Duplicates}.",
                candidate.Feature.ProductName,
                candidate.Feature.FeatureName,
                candidate.Feature.Key,
                candidate.SessionCount,
                candidate.UniqueUserCount,
                string.Join(
                    "; ",
                    candidate.DuplicateUsers.Select(duplicate =>
                        $"{duplicate.Identity} [{string.Join(',', duplicate.SessionIds)}]")));
        }
    }
}
