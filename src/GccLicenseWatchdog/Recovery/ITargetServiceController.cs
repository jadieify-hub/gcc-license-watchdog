namespace GccLicenseWatchdog.Recovery;

public enum TargetServiceState
{
    Missing,
    Stopped,
    StartPending,
    Running,
    StopPending,
    Paused,
    Unknown
}

public interface ITargetServiceController
{
    Task<TargetServiceState> GetStateAsync(CancellationToken cancellationToken);
    Task RequestStopAsync(CancellationToken cancellationToken);
    Task RequestStartAsync(CancellationToken cancellationToken);
}

public interface IWatchdogClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemWatchdogClock(TimeProvider timeProvider) : IWatchdogClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
