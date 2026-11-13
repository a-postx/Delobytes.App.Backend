using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Numbering;

/// <summary>
/// Resolves the numbering scheme the catalogue issues product codes from.
/// </summary>
public interface INumberingSchemeProvider
{
    /// <summary>
    /// Returns the tenant's default product scheme, creating the standard seed when the tenant has
    /// no default yet. An existing inactive default is reported instead of being replaced.
    /// </summary>
    public Task<NumberingScheme> GetDefaultProductSchemeAsync(CancellationToken cancellationToken);
}
