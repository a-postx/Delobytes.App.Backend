using Delobytes.App.Backend.Integrations.Application.Commands.StartOrdersImport;
using Delobytes.App.Backend.Integrations.Application.Commands.StartProductsImport;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartJob;

/// <summary>
/// Dispatches to the type-specific start command. The unified endpoint must not know how each
/// job type bootstraps itself, so this stays a thin router over the existing handlers.
/// </summary>
public class StartJobCommandHandler : IRequestHandler<StartJobCommand, StartJobResponse>
{
    private readonly IMediator _mediator;

    public StartJobCommandHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<StartJobResponse> Handle(StartJobCommand request, CancellationToken cancellationToken)
    {
        if (!request.JobType.IsManuallyStartable())
        {
            throw new InvalidOperationException(
                $"Задача типа '{request.JobType.ToStringCamelCase()}' не может быть запущена вручную: " +
                "она выполняется планировщиком по расписанию.");
        }

        if (request.JobType != JobType.ProductsImport)
        {
            throw new InvalidOperationException(
                $"Запуск задач типа '{request.JobType.ToStringCamelCase()}' через API не поддерживается.");
        }

        StartProductsImportResponse response = await _mediator.Send(
            new StartProductsImportCommand { ConnectionId = request.ConnectionId },
            cancellationToken);

        return new StartJobResponse
        {
            JobRunId = response.JobRunId,
            Type = request.JobType.ToStringCamelCase(),
        };
    }
}
