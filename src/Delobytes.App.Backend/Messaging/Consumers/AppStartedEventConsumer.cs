using Delobytes.App.Backend.Messaging.Events;
using MassTransit;

namespace Delobytes.App.Backend.Messaging.Consumers;

/// <summary>
/// Консьюмер тестового события запуска приложения.
/// </summary>
public class AppStartedEventConsumer : IConsumer<AppStartedEvent>
{
    private readonly ILogger<AppStartedEventConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppStartedEventConsumer"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public AppStartedEventConsumer(ILogger<AppStartedEventConsumer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task Consume(ConsumeContext<AppStartedEvent> context)
    {
        AppStartedEvent evt = context.Message;

        _logger.LogInformation(
            "AppStartedEvent received. EventId={EventId}, StartedAt={StartedAt}, Version={Version}",
            evt.EventId,
            evt.StartedAt,
            evt.ApplicationVersion);

        return Task.CompletedTask;
    }
}
