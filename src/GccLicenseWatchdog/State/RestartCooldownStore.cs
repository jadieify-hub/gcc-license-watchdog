using System.Text.Json;
using System.Text.Json.Serialization;

namespace GccLicenseWatchdog.State;

public interface IRestartCooldownStore
{
    Task<bool> IsActiveAsync(
        DateTimeOffset nowUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken);

    Task MarkSucceededAsync(
        DateTimeOffset restartedAtUtc,
        CancellationToken cancellationToken);
}

public sealed class RestartCooldownStore(
    string statePath,
    ILogger<RestartCooldownStore> logger) : IRestartCooldownStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _malformedBlockedUntilUtc;

    public async Task<bool> IsActiveAsync(
        DateTimeOffset nowUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_malformedBlockedUntilUtc > nowUtc)
            {
                return true;
            }

            _malformedBlockedUntilUtc = null;
            if (!File.Exists(statePath))
            {
                return false;
            }

            try
            {
                await using var stream = File.OpenRead(statePath);
                var state = await JsonSerializer.DeserializeAsync<CooldownState>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                if (state is null)
                {
                    throw new JsonException("Cooldown state is empty.");
                }

                return nowUtc < state.LastSuccessfulRestartUtc + cooldown;
            }
            catch (JsonException exception)
            {
                logger.LogError(exception, "Cooldown state is malformed; restart is temporarily blocked.");
                _malformedBlockedUntilUtc = nowUtc + cooldown;
                QuarantineMalformedState(nowUtc);
                return true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkSucceededAsync(
        DateTimeOffset restartedAtUtc,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(statePath)
                ?? throw new InvalidOperationException("Cooldown state path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(statePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        new CooldownState(restartedAtUtc),
                        JsonOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, statePath, overwrite: true);
                _malformedBlockedUntilUtc = null;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void QuarantineMalformedState(DateTimeOffset detectedAtUtc)
    {
        try
        {
            var quarantinePath = $"{statePath}.invalid-{detectedAtUtc:yyyyMMddHHmmssfff}";
            File.Move(statePath, quarantinePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Failed to quarantine malformed cooldown state at {StatePath}.", statePath);
        }
    }

    private sealed record CooldownState(
        [property: JsonPropertyName("lastSuccessfulRestartUtc")]
        DateTimeOffset LastSuccessfulRestartUtc);
}
