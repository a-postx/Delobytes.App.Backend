using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates;

/// <summary>
/// Shared mapping of wage versions to the client-facing DTO.
/// </summary>
internal static class WorkRateVersionMapper
{
    public static WorkRateVersionDto? Map(WorkRateVersion? version)
    {
        if (version == null)
        {
            return null;
        }

        return new WorkRateVersionDto
        {
            Id = version.Id,
            DailyWage = version.DailyWage,
            ValidFrom = version.ValidFrom.ToString("yyyy-MM-dd"),
        };
    }
}
