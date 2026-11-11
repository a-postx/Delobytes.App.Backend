using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure;

/// <summary>
/// Заглушки тенанта и его календарной даты: тест не должен зависеть от часов машины.
/// </summary>
internal static class TenantTimeTestDoubles
{
    /// <summary>
    /// Контекст с фиксированным идентификатором тенанта.
    /// </summary>
    public static Mock<ITenantContext> TenantContext(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(context => context.TenantId).Returns(tenantId);
        return mock;
    }

    /// <summary>
    /// Сервис времени, у которого «сегодня» и часовой пояс тенанта заданы тестом.
    /// </summary>
    public static Mock<ITenantTimeService> TenantTimeService(
        Guid tenantId,
        DateOnly today,
        string timeZoneId = "Europe/Samara")
    {
        Mock<ITenantTimeService> mock = new Mock<ITenantTimeService>();
        mock.Setup(service => service.GetTodayAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(today);
        mock.Setup(service => service.GetTimeZoneAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantTimeZones.Resolve(timeZoneId));
        return mock;
    }
}
