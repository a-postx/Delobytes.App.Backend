using System;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using FluentValidation.TestHelper;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Commands;

/// <summary>
/// The update validator is the edge that keeps values the columns cannot store out of the database:
/// its limits must match ProductConfiguration, and a packing unit must not be saved as a
/// zero-sized one. A direct API call bypasses the frontend, so the rules are pinned here.
/// </summary>
public class UpdateProductCommandValidatorTests
{
    private readonly UpdateProductCommandValidator _validator = new ();

    [Fact]
    public void DescriptionOfFiveThousandCharacters_IsAccepted()
    {
        // The import stores descriptions up to 20000; a validator stuck at the old 2000 would make
        // such a product unsavable by its owner.
        _validator.TestValidate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            Description = new string('x', 5000),
        }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void NameOfFifteenHundredCharacters_IsAccepted()
    {
        _validator.TestValidate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            Name = new string('x', 1500),
        }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PackingUnitWithZeroDimension_IsRejected()
    {
        // A zero is not "no size": the columns are non-nullable decimals, so it would be stored
        // and read back as a real 0 cm measurement.
        _validator.TestValidate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            PackingUnit = new PackingUnitDto
            {
                LengthCm = 0m,
                WidthCm = 10m,
                HeightCm = 10m,
            },
        }).ShouldHaveValidationErrorFor(x => x.PackingUnit!.LengthCm);
    }

    [Fact]
    public void PackingUnitWithPositiveDimensionsAndNoWeight_IsAccepted()
    {
        _validator.TestValidate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            PackingUnit = new PackingUnitDto
            {
                LengthCm = 10m,
                WidthCm = 20m,
                HeightCm = 30m,
                WeightKg = null,
            },
        }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OmittedPackingUnit_IsAccepted()
    {
        // A product may legitimately have no canonical row at all, so absent is not an error.
        _validator.TestValidate(new UpdateProductCommand
        {
            Id = Guid.NewGuid(),
            Name = "Renamed",
        }).ShouldNotHaveAnyValidationErrors();
    }
}
