using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog;

public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    public string ApiBaseUrl { get; init; } = "http://localhost:3189";
    public string TargetServiceName { get; init; } = "Guardant Control Center";
    public int PollIntervalSeconds { get; init; } = 30;
    public int CooldownMinutes { get; init; } = 5;
    public int StopTimeoutSeconds { get; init; } = 30;
    public int StartTimeoutSeconds { get; init; } = 30;
    public int ApiReadyTimeoutSeconds { get; init; } = 60;
    public int ApiRequestTimeoutSeconds { get; init; } = 10;
    public int SessionPageSize { get; init; } = 100;
    public int MaxSessionPages { get; init; } = 100;
}

public sealed class WatchdogOptionsValidator : IValidateOptions<WatchdogOptions>
{
    public ValidateOptionsResult Validate(string? name, WatchdogOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var apiUri) ||
            apiUri.Scheme != Uri.UriSchemeHttp ||
            !apiUri.IsLoopback)
        {
            failures.Add("Watchdog:ApiBaseUrl must be an absolute HTTP loopback address.");
        }

        if (string.IsNullOrWhiteSpace(options.TargetServiceName))
        {
            failures.Add("Watchdog:TargetServiceName must not be empty.");
        }

        ValidateRange(options.PollIntervalSeconds, 5, int.MaxValue, "PollIntervalSeconds", failures);
        ValidateRange(options.CooldownMinutes, 1, int.MaxValue, "CooldownMinutes", failures);
        ValidateRange(options.StopTimeoutSeconds, 1, int.MaxValue, "StopTimeoutSeconds", failures);
        ValidateRange(options.StartTimeoutSeconds, 1, int.MaxValue, "StartTimeoutSeconds", failures);
        ValidateRange(options.ApiReadyTimeoutSeconds, 1, int.MaxValue, "ApiReadyTimeoutSeconds", failures);
        ValidateRange(options.ApiRequestTimeoutSeconds, 1, 300, "ApiRequestTimeoutSeconds", failures);
        ValidateRange(options.SessionPageSize, 1, 1000, "SessionPageSize", failures);
        ValidateRange(options.MaxSessionPages, 1, 100, "MaxSessionPages", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string propertyName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"Watchdog:{propertyName} must be between {minimum} and {maximum}.");
        }
    }
}
