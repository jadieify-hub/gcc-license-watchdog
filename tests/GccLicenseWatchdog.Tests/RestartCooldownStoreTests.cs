using GccLicenseWatchdog.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace GccLicenseWatchdog.Tests;

public sealed class RestartCooldownStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"gcc-watchdog-tests-{Guid.NewGuid():N}");

    private string StatePath => Path.Combine(_directory, "state.json");

    [Fact]
    public async Task MissingStateIsInactive()
    {
        var store = CreateStore();

        var active = await store.IsActiveAsync(
            DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.False(active);
    }

    [Fact]
    public async Task RecentRestartIsActiveAndExpiredRestartIsInactive()
    {
        var store = CreateStore();
        await store.MarkSucceededAsync(
            DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
            CancellationToken.None);

        Assert.True(await store.IsActiveAsync(
            DateTimeOffset.Parse("2026-08-25T10:04:59Z"),
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
        Assert.False(await store.IsActiveAsync(
            DateTimeOffset.Parse("2026-08-25T10:05:00Z"),
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
    }

    [Fact]
    public async Task SuccessfulRestartPersistsAcrossStoreInstances()
    {
        await CreateStore().MarkSucceededAsync(
            DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
            CancellationToken.None);

        var active = await CreateStore().IsActiveAsync(
            DateTimeOffset.Parse("2026-08-25T10:01:00Z"),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.True(active);
    }

    [Fact]
    public async Task MarkSucceededLeavesValidStateAndNoTemporaryFile()
    {
        var store = CreateStore();

        await store.MarkSucceededAsync(
            DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(StatePath);
        Assert.Contains("2026-08-25T10:00:00", json, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task MalformedStateBlocksOneCooldownWindowThenBecomesInactive()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StatePath, "not-json");
        var store = CreateStore();
        var detectedAt = DateTimeOffset.Parse("2026-08-25T10:00:00Z");

        Assert.True(await store.IsActiveAsync(
            detectedAt,
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
        Assert.False(await store.IsActiveAsync(
            detectedAt.AddMinutes(5),
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
        Assert.False(File.Exists(StatePath));
        Assert.Single(Directory.GetFiles(_directory, "state.json.invalid-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RestartCooldownStore CreateStore() =>
        new(StatePath, NullLogger<RestartCooldownStore>.Instance);
}
