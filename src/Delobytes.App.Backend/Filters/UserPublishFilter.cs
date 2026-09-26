using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Interfaces;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit publish filter that adds the user ID to message headers.
/// Extracts it from <see cref="IUserContext"/> and adds it to every published message,
/// so that a downstream consumer can attribute its writes to the user who started the flow.
/// Mirrors <see cref="TenantPublishFilter{T}"/>.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class UserPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly IUserContext _userContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPublishFilter{T}"/> class.
    /// </summary>
    /// <param name="userContext">User context.</param>
    public UserPublishFilter(IUserContext userContext)
    {
        _userContext = userContext;
    }

    /// <inheritdoc/>
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        Guid? userId = _userContext.UserId;

        if (userId.HasValue)
        {
            context.Headers.Set(UserMessageHeaders.UserId, userId.Value);
        }

        await next.Send(context);
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("userPublish");
    }
}
