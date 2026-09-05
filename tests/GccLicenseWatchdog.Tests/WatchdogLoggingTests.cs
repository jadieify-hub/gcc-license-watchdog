using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GccLicenseWatchdog.Tests;

public sealed class WatchdogLoggingTests
{
    [Theory]
    [InlineData("Debug", true)]
    [InlineData("Warning", false)]
    public void FileLogUsesConfiguredLevelAndCategoryOverride(string level, bool includesDebug)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gcc-logging-tests-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = level,
                ["Logging:LogLevel:System.Net.Http.HttpClient"] = "Warning"
            }).Build();
            using (var fileLogger = WatchdogLogging.CreateFileLogger(directory))
            using (var factory = LoggerFactory.Create(logging =>
            {
                logging.AddConfiguration(configuration.GetSection("Logging"));
                WatchdogLogging.Configure(logging, fileLogger);
            }))
            {
                var logger = factory.CreateLogger("GccLicenseWatchdog.Tests");
                logger.LogDebug("debug-marker");
                logger.LogWarning("warning-marker");
                factory.CreateLogger("System.Net.Http.HttpClient.Guardant")
                    .LogInformation("http-info-marker");
            }

            var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.log")));
            Assert.Equal(includesDebug, log.Contains("debug-marker", StringComparison.Ordinal));
            Assert.Contains("warning-marker", log, StringComparison.Ordinal);
            Assert.DoesNotContain("http-info-marker", log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
