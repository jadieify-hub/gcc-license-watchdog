using System.Text.Json;

namespace GccLicenseWatchdog.Guardant;

internal sealed class FeaturesResponseDto
{
    public List<FeatureDto> Features { get; init; } = [];
}

internal sealed class SessionsResponseDto
{
    public List<SessionDto> Sessions { get; init; } = [];
}

internal sealed class FeatureDto
{
    public ulong DongleId { get; init; }
    public int FeatureNumber { get; init; }
    public FeatureFlagsDto Flags { get; init; } = new();
    public int FloatingResource { get; init; }
    public int MaxConcurrentResource { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ProductModification { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int ProductNumber { get; init; }
    public int RemoteMode { get; init; }
    public int SessionsCount { get; init; }
    public VendorDto Vendor { get; init; } = new();
}

internal sealed class FeatureFlagsDto
{
    public bool Remote { get; init; }
}

internal sealed class VendorDto
{
    public uint PublicCode { get; init; }
}

internal sealed class SessionDto
{
    public FeatureDto Feature { get; init; } = new();
    public long IssueTime { get; init; }
    public int? ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public long SessionId { get; init; }
    public UserDto User { get; init; } = new();
}

internal sealed class UserDto
{
    public JsonElement Id { get; init; }
    public string? Name { get; init; }
}
