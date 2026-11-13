using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

/// <summary>
/// Validates the fields that are present in a partial update. A property the caller did not send
/// arrives as null and is skipped by <c>When</c>, so a request that only changes the SKU is not
/// rejected for the name it never carried.
///
/// SKU uniqueness is deliberately not checked here. The authoritative enforcement is the unique
/// index on (TenantId, Sku), surfaced as the 409 <c>catalog.product.sku_conflict</c> by
/// UniqueConstraintTranslator. A pre-check in the validator would only add a race window.
/// </summary>
public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    // Mirrors ProductConfiguration: Sku HasMaxLength(100), Name HasMaxLength(2000),
    // Description HasMaxLength(20000). A name or description the import legitimately stored at
    // its own limit must remain editable, so these must not be narrower than the columns.
    private const int SkuMaxLength = 100;
    private const int NameMaxLength = 2000;
    private const int DescriptionMaxLength = 20000;

    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();

        // Empty is rejected rather than treated as "clear the SKU": the column is required, and a
        // product without a SKU cannot be identified in the catalog list.
        RuleFor(x => x.Sku)
            .Must(sku => !string.IsNullOrWhiteSpace(sku))
            .WithMessage("SKU не может быть пустым.")
            .MaximumLength(SkuMaxLength)
            .When(x => x.Sku != null);

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Название не может быть пустым.")
            .MaximumLength(NameMaxLength)
            .When(x => x.Name != null);

        // An empty description is a legitimate value: it clears the field.
        RuleFor(x => x.Description)
            .MaximumLength(DescriptionMaxLength)
            .When(x => x.Description != null);

        RuleForEach(x => x.Barcodes)
            .ChildRules(barcode =>
            {
                barcode.RuleFor(b => b.Value)
                    .NotEmpty()
                    .MaximumLength(100);
            })
            .When(x => x.Barcodes != null);

        // The canonical unit's dimensions are non-nullable decimals, so a zero cannot represent
        // absence: it would be stored and read back as a real 0 cm size. The import treats zero as
        // "unknown" and writes no row; a manual update must reject it instead of writing one.
        RuleFor(x => x.PackingUnit!.LengthCm)
            .GreaterThan(0)
            .WithMessage("Длина упаковки должна быть больше нуля.")
            .When(x => x.PackingUnit != null);

        RuleFor(x => x.PackingUnit!.WidthCm)
            .GreaterThan(0)
            .WithMessage("Ширина упаковки должна быть больше нуля.")
            .When(x => x.PackingUnit != null);

        RuleFor(x => x.PackingUnit!.HeightCm)
            .GreaterThan(0)
            .WithMessage("Высота упаковки должна быть больше нуля.")
            .When(x => x.PackingUnit != null);

        RuleFor(x => x.PackingUnit!.WeightKg)
            .Must(weight => !weight.HasValue || weight.Value > 0)
            .WithMessage("Вес упаковки должен быть больше нуля.")
            .When(x => x.PackingUnit != null);
    }
}
