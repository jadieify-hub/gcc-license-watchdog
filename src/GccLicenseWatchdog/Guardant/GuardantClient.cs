using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog.Guardant;

public sealed class GuardantClient(
    HttpClient httpClient,
    IOptions<WatchdogOptions> options,
    ILogger<GuardantClient> logger) : IGuardantClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WatchdogOptions _options = options.Value;

    public async Task<IReadOnlyList<FeatureInfo>> GetFeaturesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/v1.0/lm/features", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await DeserializeAsync<FeaturesResponseDto>(response, cancellationToken);
        return payload.Features.Select(MapFeature).ToArray();
    }

    public async Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = new List<SessionInfo>();
        for (var page = 1; page <= _options.MaxSessionPages; page++)
        {
            using var response = await httpClient.GetAsync(
                $"/v1.0/lm/sessions?page={page}&limit={_options.SessionPageSize}",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await DeserializeAsync<SessionsResponseDto>(response, cancellationToken);
            sessions.AddRange(payload.Sessions.Select(MapSession));

            var totalCount = ReadTotalCount(response);
            if (totalCount.HasValue && sessions.Count >= totalCount.Value)
            {
                return sessions;
            }

            if (payload.Sessions.Count < _options.SessionPageSize)
            {
                return sessions;
            }

            if (page == _options.MaxSessionPages)
            {
                throw new InvalidOperationException(
                    $"Guardant sessions page limit ({_options.MaxSessionPages}) was exceeded.");
            }
        }

        return sessions;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await GetFeaturesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Guardant API health check failed.");
            return false;
        }
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("Guardant API returned an empty JSON payload.");
    }

    private static int? ReadTotalCount(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Total-Count", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
    }

    private static FeatureInfo MapFeature(FeatureDto feature) => new(
        CreateKey(feature),
        feature.ProductName,
        feature.Name,
        feature.RemoteMode,
        feature.Flags.Remote,
        feature.FloatingResource,
        feature.MaxConcurrentResource,
        feature.SessionsCount);

    private static SessionInfo MapSession(SessionDto session) => new(
        session.SessionId,
        CreateKey(session.Feature),
        NormalizeJsonId(session.User.Id),
        session.User.Name,
        session.IssueTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(session.IssueTime) : null,
        session.ProcessName,
        session.ProcessId);

    private static FeatureKey CreateKey(FeatureDto feature) => new(
        feature.Vendor.PublicCode,
        feature.DongleId,
        feature.ProductNumber,
        feature.ProductModification,
        feature.FeatureNumber);

    private static string? NormalizeJsonId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(id.GetString()) ? null : id.GetString(),
        JsonValueKind.Number => id.GetRawText(),
        _ => null
    };
}
