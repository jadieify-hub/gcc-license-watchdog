using System.Net;
using GccLicenseWatchdog.Guardant;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog.Tests;

public sealed class GuardantClientTests
{
    [Fact]
    public async Task GetFeaturesMapsLocalFeatureAndNumericFields()
    {
        const string json = """
        {
          "features": [{
            "consumptionMode": 1,
            "currentRunCounterValue": 0,
            "detachedResource": 0,
            "dongleId": 9876543210,
            "featureNumber": 2,
            "flags": { "expired": false, "remote": false },
            "floatingResource": 0,
            "maxConcurrentResource": 11,
            "maxRunCounter": 0,
            "name": "ДАЛИОН: ТРЕНД",
            "productModification": 9,
            "productName": "ДАЛИОН",
            "productNumber": 1,
            "remoteMode": 3,
            "reservedResource": 0,
            "restOfLifeTimeDays": 0,
            "sessionsCount": 11,
            "validFromDate": 0,
            "validUpToDate": 0,
            "vendor": { "publicCode": 1234567890, "publicCodeText": "TEST001" }
          }]
        }
        """;
        using var handler = new ScriptedHttpMessageHandler(Response(json));
        var client = CreateClient(handler);

        var features = await client.GetFeaturesAsync(CancellationToken.None);

        var feature = Assert.Single(features);
        Assert.Equal(new FeatureKey(1234567890, 9876543210, 1, 9, 2), feature.Key);
        Assert.Equal("ДАЛИОН", feature.ProductName);
        Assert.Equal("ДАЛИОН: ТРЕНД", feature.FeatureName);
        Assert.Equal(3, feature.RemoteMode);
        Assert.False(feature.IsRemote);
        Assert.Equal(0, feature.FloatingResource);
        Assert.Equal(11, feature.MaxConcurrentResource);
        Assert.Equal(11, feature.SessionsCount);
    }

    [Fact]
    public async Task GetSessionsReadsNumericAndStringUserIds()
    {
        const string json = """
        {
          "sessions": [
            {
              "feature": {
                "dongleId": 9876543210,
                "featureNumber": 2,
                "productModification": 9,
                "productNumber": 1,
                "vendor": { "publicCode": 1234567890 }
              },
              "issueTime": 1700000000,
              "processId": 4321,
              "processName": "rphost",
              "sessionId": 101,
              "user": { "id": 10001, "name": "Тестовый пользователь А (11111)" }
            },
            {
              "feature": {
                "dongleId": 9876543210,
                "featureNumber": 2,
                "productModification": 9,
                "productNumber": 1,
                "vendor": { "publicCode": 1234567890 }
              },
              "issueTime": 1700000001,
              "processId": 4321,
              "processName": "rphost",
              "sessionId": 102,
              "user": { "id": "10002", "name": "Тестовый пользователь Б (22222)" }
            }
          ]
        }
        """;
        using var handler = new ScriptedHttpMessageHandler(Response(json, totalCount: 2));
        var client = CreateClient(handler);

        var sessions = await client.GetAllSessionsAsync(CancellationToken.None);

        Assert.Collection(
            sessions,
            first =>
            {
                Assert.Equal(101, first.SessionId);
                Assert.Equal("10001", first.UserId);
                Assert.Equal("Тестовый пользователь А (11111)", first.UserName);
                Assert.Equal("rphost", first.ProcessName);
                Assert.Equal(4321, first.ProcessId);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), first.IssuedAt);
            },
            second =>
            {
                Assert.Equal(102, second.SessionId);
                Assert.Equal("10002", second.UserId);
            });
    }

    [Fact]
    public async Task GetSessionsFollowsPaginationUntilTotalCount()
    {
        using var handler = new ScriptedHttpMessageHandler(
            Response(SessionsJson(1, "one"), totalCount: 3),
            Response(SessionsJson(2, "two"), totalCount: 3),
            Response(SessionsJson(3, "three"), totalCount: 3));
        var client = CreateClient(handler, pageSize: 1);

        var sessions = await client.GetAllSessionsAsync(CancellationToken.None);

        Assert.Equal([1L, 2L, 3L], sessions.Select(session => session.SessionId));
        Assert.Equal(
            [
                "/v1.0/lm/sessions?page=1&limit=1",
                "/v1.0/lm/sessions?page=2&limit=1",
                "/v1.0/lm/sessions?page=3&limit=1"
            ],
            handler.RequestUris.Select(uri => uri.PathAndQuery));
    }

    [Fact]
    public async Task GetSessionsThrowsWhenConfiguredPageLimitIsExceeded()
    {
        using var handler = new ScriptedHttpMessageHandler(
            Response(SessionsJson(1, "one"), totalCount: 2));
        var client = CreateClient(handler, pageSize: 1, maxSessionPages: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAllSessionsAsync(CancellationToken.None));

        Assert.Contains("page limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    public async Task IsHealthyReturnsFalseForHttpOrJsonFailure(HttpStatusCode statusCode, string body)
    {
        using var handler = new ScriptedHttpMessageHandler(Response(body, statusCode));
        var client = CreateClient(handler);

        var healthy = await client.IsHealthyAsync(CancellationToken.None);

        Assert.False(healthy);
    }

    private static GuardantClient CreateClient(
        HttpMessageHandler handler,
        int pageSize = 100,
        int maxSessionPages = 100)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:3189")
        };
        var options = Options.Create(new WatchdogOptions
        {
            ApiBaseUrl = "http://localhost:3189",
            SessionPageSize = pageSize,
            MaxSessionPages = maxSessionPages
        });
        return new GuardantClient(httpClient, options, NullLogger<GuardantClient>.Instance);
    }

    private static HttpResponseMessage Response(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        int? totalCount = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
        if (totalCount.HasValue)
        {
            response.Headers.Add("X-Total-Count", totalCount.Value.ToString());
        }

        return response;
    }

    private static string SessionsJson(long sessionId, string userId) => $$"""
        {
          "sessions": [{
            "feature": {
              "dongleId": 9876543210,
              "featureNumber": 2,
              "productModification": 9,
              "productNumber": 1,
              "vendor": { "publicCode": 1234567890 }
            },
            "issueTime": 1700000000,
            "processId": 4321,
            "processName": "rphost",
            "sessionId": {{sessionId}},
            "user": { "id": "{{userId}}", "name": "User {{userId}}" }
          }]
        }
        """;
}

internal sealed class ScriptedHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    public List<Uri> RequestUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri ?? throw new InvalidOperationException("Request URI is missing."));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No scripted response remains.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
