using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

/// <summary>
/// Validates the fields a manual product creation carries. The code is not among them: it is
/// issued by the numbering scheme inside the handler and never accepted from a client.
///
/// SKU uniqueness is deliberately not checked here. The authoritative enforcement is the unique
/// index on (TenantId, Sku), and the handler answers a taken SKU with the 409
/// <c>catalog.product.sku_conflict</c> before inserting. A validator pre-check would only add a
/// race window, see <see cref="UpdateProductCommandValidator"/>.
/// </summary>
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    // Mirrors ProductConfiguration: Sku HasMaxLength(100), Name HasMaxLength(2000),
    // Description HasMaxLength(20000).
    private const int SkuMaxLength = 100;
    private const int NameMaxLength = 2000;
    private const int DescriptionMaxLength = 20000;

    public CreateProductCommandValidator()
    {
        // Both columns are required: an empty SKU or name would be stored, but could never be
        // distinguished from a missing value by the client that sent it.
        RuleFor(x => x.Sku)
            .Must(sku => !string.IsNullOrWhiteSpace(sku))
            .WithMessage("SKU не может быть пустым.")
            .MaximumLength(SkuMaxLength);

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Название не может быть пустым.")
            .MaximumLength(NameMaxLength);

        // An empty description is a legitimate value for a newly created product.
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

        // PackingUnit.LengthCm/WidthCm/HeightCm are non-nullable decimals, so a zero can only mean
        // "not supplied" here while the column would read it back as a real zero size. The import
        // treats zero as "unknown" and writes no row; a manual create rejects it instead.
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
