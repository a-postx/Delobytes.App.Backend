using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Period diagnostics.</summary>
public sealed class GetPnlDiagnosticsQuery : IRequest<PnlDiagnosticsResult>
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }
}

/// <summary>Handles <see cref="GetPnlDiagnosticsQuery"/>.</summary>
public sealed class GetPnlDiagnosticsQueryHandler : IRequestHandler<GetPnlDiagnosticsQuery, PnlDiagnosticsResult>
{
    private readonly IPnlReadStore _store;

    public GetPnlDiagnosticsQueryHandler(IPnlReadStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public Task<PnlDiagnosticsResult> Handle(GetPnlDiagnosticsQuery request, CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            throw new ArgumentException("The period start must not be after its end.", nameof(request));
        }

        return _store.GetDiagnosticsAsync(request.From, request.To, cancellationToken);
    }
}
