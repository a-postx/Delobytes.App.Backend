using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Policies;

/// <summary>
/// Retry policies for HTTP clients.
/// </summary>
public static class RetryPolicies
{
    /// <summary>
    /// Gets retry policy with exponential backoff for HTTP requests.
    /// </summary>
    /// <returns>Retry policy pipeline.</returns>
    public static ResiliencePipeline<HttpResponseMessage> GetRetryPolicy(ILogger logger)
    {
        ResiliencePipeline<HttpResponseMessage> pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                OnRetry = arguments =>
                {
                    logger.LogWarning(
                        "HTTP request retry attempt {AttemptNumber} after {RetryDelay}ms due to {ExceptionType}: {ExceptionMessage}",
                        arguments.AttemptNumber,
                        arguments.RetryDelay.TotalMilliseconds,
                        arguments.Outcome.Exception?.GetType().Name,
                        arguments.Outcome.Exception?.Message);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        return pipeline;
    }
}
