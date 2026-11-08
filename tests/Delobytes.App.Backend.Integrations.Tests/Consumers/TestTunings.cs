using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Builds the same resolved tuning the container would hand a fetcher, so a test that varies
/// options exercises the production normalization instead of restating it.
/// </summary>
internal static class TestTunings
{
    public static OrdersImportTuning Wildberries(WildberriesImportOptions? options = null)
    {
        return (options ?? new WildberriesImportOptions()).ToTuning(JobType.WbOrdersImport);
    }

    public static OrdersImportTuning Ozon(
        OzonImportOptions? options = null, JobType jobType = JobType.OzonOrdersImportFbo)
    {
        return (options ?? new OzonImportOptions()).ToTuning(jobType);
    }
}
