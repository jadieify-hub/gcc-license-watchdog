namespace GccLicenseWatchdog.Guardant;

public readonly record struct FeatureKey(
    uint VendorPublicCode,
    ulong DongleId,
    int ProductNumber,
    int ProductModification,
    int FeatureNumber);

public sealed record FeatureInfo(
    FeatureKey Key,
    string ProductName,
    string FeatureName,
    int RemoteMode,
    bool IsRemote,
    int FloatingResource,
    int MaxConcurrentResource,
    int SessionsCount);

public sealed record SessionInfo(
    long SessionId,
    FeatureKey FeatureKey,
    string? UserId,
    string? UserName,
    DateTimeOffset? IssuedAt,
    string? ProcessName,
    int? ProcessId);
