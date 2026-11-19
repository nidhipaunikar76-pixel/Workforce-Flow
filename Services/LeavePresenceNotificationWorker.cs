using Microsoft.Extensions.DependencyInjection;

namespace Workforce_Flow.Services;

public sealed class LeavePresenceNotificationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LeavePresenceNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessAsync(stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<LeavePresenceNotificationService>();
            await dispatcher.ProcessDueNotificationsAsync(DateTime.Now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to process leave presence notifications.");
        }
    }
}