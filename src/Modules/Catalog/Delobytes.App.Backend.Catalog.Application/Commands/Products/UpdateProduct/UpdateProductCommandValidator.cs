using FluentValidation;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;

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
    // Mirrors ProductConfiguration: Sku HasMaxLength(100), Name HasMaxLength(200).
    private const int SkuMaxLength = 100;
    private const int NameMaxLength = 200;
    private const int DescriptionMaxLength = 2000;

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
    }
}
