using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// Resolves a tenant's default product numbering scheme and seeds the standard one on first use.
/// </summary>
/// <remarks>
/// Seeding is lazy because tenant creation cannot create these rows: <c>Contracts</c> carries no
/// tenant-created event and Catalog must not reference Identity. A tenant that already holds
/// products receives its scheme from the product-code migration instead, with a counter placed
/// past the backfilled codes — this provider never resets an existing counter, it only looks up
/// and seeds.
/// </remarks>
public class NumberingSchemeProvider : INumberingSchemeProvider
{
    private const string ProductSchemeCode = "PRODUCT";
    private const string ProductSchemeName = "Product code";
    private const string ProductSequenceName = "Product code";
    private const string ProductCodeConstant = "P-";
    private const int ProductCodeAutoNumberLength = 6;

    private readonly CatalogDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IValidator<NumberingScheme> _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumberingSchemeProvider"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    /// <param name="tenantContext">Current tenant context, used to describe a misconfigured default.</param>
    /// <param name="validator">Validator enforcing the scheme invariants.</param>
    public NumberingSchemeProvider(
        CatalogDbContext context,
        ITenantContext tenantContext,
        IValidator<NumberingScheme> validator)
    {
        _context = context;
        _tenantContext = tenantContext;
        _validator = validator;
    }

    /// <inheritdoc/>
    public async Task<NumberingScheme> GetDefaultProductSchemeAsync(CancellationToken cancellationToken)
    {
        NumberingScheme? existing = await _context.NumberingSchemes
            .Include(scheme => scheme.Segments)
                .ThenInclude(segment => segment.NumberingSequence)
            .Where(scheme => scheme.EntityKind == NumberingEntityKind.Product && scheme.IsDefault)
            .OrderBy(scheme => scheme.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                throw new InvalidOperationException(
                    $"The default numbering scheme '{existing.Code}' ({existing.Id}) for " +
                    $"{existing.EntityKind} of tenant '{_tenantContext.TenantId}' is inactive. " +
                    "Activate it or assign another default scheme; product codes cannot be issued without one.");
            }

            return existing;
        }

        return await CreateDefaultProductSchemeAsync(cancellationToken);
    }

    private async Task<NumberingScheme> CreateDefaultProductSchemeAsync(CancellationToken cancellationToken)
    {
        NumberingSequence sequence = new NumberingSequence
        {
            Id = Guid.NewGuid(),
            Name = ProductSequenceName,
            StartNumber = 1,
            Step = 1,
            NextNumber = 1,
            IsActive = true,
        };

        NumberingScheme scheme = new NumberingScheme
        {
            Id = Guid.NewGuid(),
            Code = ProductSchemeCode,
            Name = ProductSchemeName,
            EntityKind = NumberingEntityKind.Product,
            IsDefault = true,
            IsActive = true,
            Segments = new List<NumberingSegment>
            {
                new NumberingSegment
                {
                    Id = Guid.NewGuid(),
                    Order = 1,
                    Type = NumberingSegmentType.Constant,
                    ConstantValue = ProductCodeConstant,
                },
                new NumberingSegment
                {
                    Id = Guid.NewGuid(),
                    Order = 2,
                    Type = NumberingSegmentType.AutoNumber,
                    NumberingSequenceId = sequence.Id,
                    NumberingSequence = sequence,
                    Length = ProductCodeAutoNumberLength,
                },
            },
        };

        await _validator.ValidateAndThrowAsync(scheme, cancellationToken);

        _context.NumberingSchemes.Add(scheme);
        await _context.SaveChangesAsync(cancellationToken);

        return scheme;
    }
}
