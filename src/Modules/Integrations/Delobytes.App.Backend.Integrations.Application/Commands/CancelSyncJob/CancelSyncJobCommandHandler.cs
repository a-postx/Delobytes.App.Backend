using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.CancelSyncJob;

public class CancelSyncJobCommandHandler : IRequestHandler<CancelSyncJobCommand>
{
    private readonly ISyncJobRepository _syncJobRepository;

    public CancelSyncJobCommandHandler(ISyncJobRepository syncJobRepository)
    {
        _syncJobRepository = syncJobRepository;
    }

    public async Task Handle(CancelSyncJobCommand request, CancellationToken cancellationToken)
    {
        SyncJob? syncJob = await _syncJobRepository.FindByIdAsync(request.SyncJobId, cancellationToken);

        if (syncJob == null)
        {
            throw new KeyNotFoundException($"Задача импорта с ID '{request.SyncJobId}' не найдена.");
        }

        if (syncJob.JobType != JobType.ProductsImport)
        {
            throw new InvalidOperationException(
                $"Отмена поддерживается только для задач типа ProductsImport. Текущий тип: '{syncJob.JobType}'.");
        }

        if (syncJob.Status != SyncJobStatus.Pending && syncJob.Status != SyncJobStatus.Running)
        {
            throw new InvalidOperationException(
                $"Задача в статусе '{syncJob.Status}' не может быть отменена. " +
                "Отмена доступна только для задач со статусом Pending или Running.");
        }

        syncJob.Status = SyncJobStatus.Cancelled;
        syncJob.CompletedAt = DateTimeOffset.UtcNow;

        _syncJobRepository.Update(syncJob);
        await _syncJobRepository.SaveChangesAsync(cancellationToken);
    }
}
