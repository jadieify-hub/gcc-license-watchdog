using GccLicenseWatchdog;
using GccLicenseWatchdog.Detection;
using GccLicenseWatchdog.Guardant;
using GccLicenseWatchdog.Recovery;
using GccLicenseWatchdog.State;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;
using Serilog;

const string watchdogServiceName = "GCC License Watchdog";
var hostArguments = args.Where(argument => !string.Equals(argument, "--console", StringComparison.OrdinalIgnoreCase)).ToArray();
using var singleInstance = new Mutex(initiallyOwned: false, name: @"Global\GCCLicenseWatchdog");
var ownsMutex = false;
try
{
    try
    {
        ownsMutex = singleInstance.WaitOne(TimeSpan.Zero);
    }
    catch (AbandonedMutexException)
    {
        ownsMutex = true;
    }

    if (!ownsMutex)
    {
        return 2;
    }

    var dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        watchdogServiceName);
    var logDirectory = Path.Combine(dataDirectory, "logs");
    Directory.CreateDirectory(logDirectory);

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.File(
            Path.Combine(logDirectory, "watchdog-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true)
        .CreateLogger();

    try
    {
        var builder = Host.CreateApplicationBuilder(hostArguments);
        builder.Configuration.AddJsonFile(
            Path.Combine(dataDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: true);
        builder.Services.AddWindowsService(settings => settings.ServiceName = watchdogServiceName);
        builder.Services.AddSerilog(Log.Logger, dispose: true);
        builder.Logging.AddEventLog(settings =>
        {
            settings.LogName = "Application";
            settings.SourceName = watchdogServiceName;
        });
        builder.Logging.AddFilter<EventLogLoggerProvider>(category: null, LogLevel.Critical);

        builder.Services
            .AddOptions<WatchdogOptions>()
            .Bind(builder.Configuration.GetSection(WatchdogOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<WatchdogOptions>, WatchdogOptionsValidator>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IWatchdogClock, SystemWatchdogClock>();
        builder.Services.AddSingleton<LicenseIncidentDetector>();
        builder.Services.AddSingleton<ITargetServiceController>(services =>
            new WindowsTargetServiceController(
                services.GetRequiredService<IOptions<WatchdogOptions>>().Value.TargetServiceName));
        builder.Services.AddSingleton<IGccRecoveryManager, GccRecoveryManager>();
        builder.Services.AddSingleton<IRestartCooldownStore>(services =>
            new RestartCooldownStore(
                Path.Combine(dataDirectory, "state.json"),
                services.GetRequiredService<ILogger<RestartCooldownStore>>()));
        builder.Services.AddSingleton<IWatchdogEngine, WatchdogEngine>();
        builder.Services.AddHostedService<WatchdogWorker>();
        builder.Services.AddHttpClient<IGuardantClient, GuardantClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<WatchdogOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.ApiRequestTimeoutSeconds);
        });

        builder.Build().Run();
        return 0;
    }
    catch (Exception exception)
    {
        Log.Fatal(exception, "GCC License Watchdog terminated unexpectedly.");
        return 1;
    }
    finally
    {
        Log.CloseAndFlush();
    }
}
finally
{
    if (ownsMutex)
    {
        singleInstance.ReleaseMutex();
    }
}
