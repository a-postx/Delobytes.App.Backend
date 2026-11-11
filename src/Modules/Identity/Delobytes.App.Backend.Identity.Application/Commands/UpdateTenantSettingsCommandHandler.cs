using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands;

/// <summary>
/// Команда изменения настроек тенанта, не относящихся к реквизитам юрлица.
/// </summary>
public class UpdateTenantSettingsCommand : IRequest<UpdateTenantSettingsResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets идентификатор часового пояса тенанта (IANA, например <c>Europe/Samara</c>).
    /// </summary>
    public string TimeZone { get; set; } = default!;

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}

/// <summary>
/// Ответ на <see cref="UpdateTenantSettingsCommand"/>.
/// </summary>
public class UpdateTenantSettingsResponse
{
    /// <summary>
    /// Gets or sets идентификатор тенанта.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets сохранённый идентификатор часового пояса.
    /// </summary>
    public string TimeZone { get; set; } = default!;
}

/// <summary>
/// Обработчик <see cref="UpdateTenantSettingsCommand"/>.
/// </summary>
public class UpdateTenantSettingsCommandHandler : IRequestHandler<UpdateTenantSettingsCommand, UpdateTenantSettingsResponse>
{
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTenantSettingsCommandHandler"/> class.
    /// </summary>
    /// <param name="tenantRepository">Репозиторий тенантов.</param>
    public UpdateTenantSettingsCommandHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc/>
    public async Task<UpdateTenantSettingsResponse> Handle(
        UpdateTenantSettingsCommand request,
        CancellationToken cancellationToken)
    {
        Domain.Entities.Tenant? tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Пространство с ID {request.TenantId} не найдено.");

        // Уже посчитанный NextRunAt действующих расписаний остаётся в старом поясе до
        // первого срабатывания: планировщик пересчитывает cron только при переносе
        // следующего запуска. Расхождение ограничено одним интервалом расписания и
        // сознательно принято — пересчёт требовал бы записи в чужие агрегаты из Identity.
        tenant.TimeZone = request.TimeZone.Trim();
        tenant.UpdatedAt = DateTimeOffset.UtcNow;

        _tenantRepository.Update(tenant);
        await _tenantRepository.SaveChangesAsync(cancellationToken);

        return new UpdateTenantSettingsResponse
        {
            TenantId = tenant.Id,
            TimeZone = tenant.TimeZone,
        };
    }
}
