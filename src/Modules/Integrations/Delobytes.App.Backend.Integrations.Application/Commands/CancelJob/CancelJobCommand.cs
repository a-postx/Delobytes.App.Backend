using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CancelJob;

/// <summary>
/// Requests cooperative cancellation of any job run. The handler decides whether the job type
/// is cancellable at all, so the endpoint stays type-agnostic.
/// </summary>
public class CancelJobCommand : IRequest
{
    public Guid JobRunId { get; set; }
}
