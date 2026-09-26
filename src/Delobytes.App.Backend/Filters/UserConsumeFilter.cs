using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Services;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit consume filter that extracts user ID from message headers
/// and sets it in <see cref="MessageUserContext"/> for the duration of message processing.
/// Mirrors <see cref="TenantConsumeFilter{T}"/>: clears the value in finally to prevent leakage.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class UserConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly MessageUserContext _messageUserContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserConsumeFilter{T}"/> class.
    /// </summary>
    /// <param name="messageUserContext">Message-based user context.</param>
    public UserConsumeFilter(MessageUserContext messageUserContext)
    {
        _messageUserContext = messageUserContext;
    }

    /// <inheritdoc/>
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        Guid? userId = context.Headers.Get<Guid>(UserMessageHeaders.UserId);

        if (userId.HasValue)
        {
            _messageUserContext.SetUserId(userId.Value);
        }

        try
        {
            await next.Send(context);
        }
        finally
        {
            // Always clear after message processing to prevent leakage into the next message.
            _messageUserContext.Clear();
        }
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("userConsume");
    }
}
