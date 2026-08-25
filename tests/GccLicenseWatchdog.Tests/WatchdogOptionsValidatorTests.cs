using Microsoft.Extensions.Options;

namespace GccLicenseWatchdog.Tests;

public sealed class WatchdogOptionsValidatorTests
{
    [Fact]
    public void RejectsNonLoopbackApiAddress()
    {
        var validator = new WatchdogOptionsValidator();
        var options = new WatchdogOptions { ApiBaseUrl = "http://192.0.2.10:3189" };

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("loopback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AcceptsSafeDefaults()
    {
        var validator = new WatchdogOptionsValidator();

        var result = validator.Validate(Options.DefaultName, new WatchdogOptions());

        Assert.True(result.Succeeded);
    }
}
