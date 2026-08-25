namespace GccLicenseWatchdog.Guardant;

public interface IGuardantClient
{
    Task<IReadOnlyList<FeatureInfo>> GetFeaturesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(CancellationToken cancellationToken);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
