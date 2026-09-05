using Microsoft.Extensions.Logging.EventLog;
using Serilog;
using Serilog.Extensions.Logging;

namespace GccLicenseWatchdog;

public static class WatchdogLogging
{
    public static Serilog.Core.Logger CreateFileLogger(string logDirectory) => new LoggerConfiguration()
        // Filtering belongs to Logging:LogLevel, before events reach this sink.
        .MinimumLevel.Verbose()
        .WriteTo.File(
            Path.Combine(logDirectory, "watchdog-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true)
        .CreateLogger();

    public static void Configure(ILoggingBuilder logging, Serilog.ILogger fileLogger)
    {
        logging.ClearProviders();
        logging.AddProvider(new SerilogLoggerProvider(fileLogger, dispose: false));
        logging.AddEventLog(settings =>
        {
            settings.LogName = "Application";
            settings.SourceName = "GCC License Watchdog";
        });
        logging.AddFilter<EventLogLoggerProvider>(category: null, LogLevel.Critical);
    }
}
