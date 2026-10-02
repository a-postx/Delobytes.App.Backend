using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.TaxProfiles.DeleteTenantTaxProfile;

/// <summary>
/// Handler for DeleteTenantTaxProfileCommand.
/// </summary>
public class DeleteTenantTaxProfileCommandHandler : IRequestHandler<DeleteTenantTaxProfileCommand, DeleteTenantTaxProfileResponse>
{
    private readonly ITenantTaxProfileRepository _taxProfileRepository;
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteTenantTaxProfileCommandHandler"/> class.
    /// </summary>
    public DeleteTenantTaxProfileCommandHandler(
        ITenantTaxProfileRepository taxProfileRepository,
        ITenantRepository tenantRepository)
    {
        _taxProfileRepository = taxProfileRepository;
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc/>
    public async Task<DeleteTenantTaxProfileResponse> Handle(
        DeleteTenantTaxProfileCommand request,
        CancellationToken cancellationToken)
    {
        TenantTaxProfile? profile = await _taxProfileRepository.FindByIdAsync(request.TenantId, request.Id, cancellationToken);

        if (profile == null)
        {
            return new DeleteTenantTaxProfileResponse { Found = false };
        }

        TenantTaxProfile? latest = await _taxProfileRepository.GetLatestAsync(request.TenantId, cancellationToken);

        // Удаление не последней версии оставило бы дыру в истории: более ранний профиль
        // начал бы применяться к периоду, который уже посчитан по удалённому.
        if (latest != null && latest.Id != profile.Id)
        {
            return new DeleteTenantTaxProfileResponse { Found = true, NotLatest = true };
        }

        Tenant? tenant = await _tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Пространство с ID {request.TenantId} не найдено.");
        }

        // Дата тенанта, а не UTC: иначе ставка, срок действия которой наступил в 00:30
        // по Москве 1 января, ещё числилась бы "будущей" по UTC (21:30 31 декабря).
        DateOnly tenantToday = ResolveTenantDate(tenant.TimeZone, DateTimeOffset.UtcNow);

        // Последнюю версию, которая уже вступила в силу, удалять нельзя: она могла
        // попасть в уже посчитанные отчёты, и её исчезновение задним числом эти отчёты
        // обесценит. Отменить можно только версию, дата начала которой ещё не настала.
        if (profile.ValidFrom <= tenantToday)
        {
            return new DeleteTenantTaxProfileResponse { Found = true, AlreadyEffective = true };
        }

        _taxProfileRepository.Remove(profile);
        await _taxProfileRepository.SaveChangesAsync(cancellationToken);

        return new DeleteTenantTaxProfileResponse { Found = true };
    }

    private static DateOnly ResolveTenantDate(string timeZoneId, DateTimeOffset moment)
    {
        try
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' не найден в системе.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"Часовой пояс '{timeZoneId}' содержит некорректные данные.", ex);
        }
    }
}
