using Delobytes.App.Backend.Contracts.Accounting;

namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Resolves the tax profile effective on a calendar date from the whole history of the tenant.
/// Reading the history once and searching it here keeps a report over a long period to a single
/// query instead of one per distinct date.
/// </summary>
public sealed class TaxProfileTimeline
{
    private readonly TenantTaxProfileSnapshot[] _profiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaxProfileTimeline"/> class.
    /// </summary>
    /// <param name="profiles">Profiles in any order; the timeline sorts them by <c>ValidFrom</c>.</param>
    public TaxProfileTimeline(IReadOnlyList<TenantTaxProfileSnapshot> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _profiles = profiles.OrderBy(profile => profile.ValidFrom).ToArray();
    }

    /// <summary>
    /// Returns the profile with the greatest <c>ValidFrom</c> that is not after the date.
    /// </summary>
    /// <param name="date">Calendar date of the tenant.</param>
    /// <returns>The effective profile, or <c>null</c> when the date precedes every profile.</returns>
    public TenantTaxProfileSnapshot? Resolve(DateOnly date)
    {
        int low = 0;
        int high = _profiles.Length - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);

            if (_profiles[middle].ValidFrom <= date)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found < 0 ? null : _profiles[found];
    }
}
