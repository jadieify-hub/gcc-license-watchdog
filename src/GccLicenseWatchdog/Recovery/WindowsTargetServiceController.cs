using System.ServiceProcess;

namespace GccLicenseWatchdog.Recovery;

public sealed class WindowsTargetServiceController(string serviceName) : ITargetServiceController
{
    public Task<TargetServiceState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();
            return Task.FromResult(Map(controller.Status));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(TargetServiceState.Missing);
        }
    }

    public Task RequestStopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var controller = new ServiceController(serviceName);
        controller.Stop();
        return Task.CompletedTask;
    }

    public Task RequestStartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var controller = new ServiceController(serviceName);
        controller.Start();
        return Task.CompletedTask;
    }

    private static TargetServiceState Map(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => TargetServiceState.Stopped,
        ServiceControllerStatus.StartPending => TargetServiceState.StartPending,
        ServiceControllerStatus.Running => TargetServiceState.Running,
        ServiceControllerStatus.StopPending => TargetServiceState.StopPending,
        ServiceControllerStatus.Paused or
        ServiceControllerStatus.PausePending or
        ServiceControllerStatus.ContinuePending => TargetServiceState.Paused,
        _ => TargetServiceState.Unknown
    };
}
