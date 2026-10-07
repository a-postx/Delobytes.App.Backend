using Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.ProductWorkRates;

/// <summary>
/// Tests for CreateProductWorkRateCommandValidator. Field-level rules only — database-dependent
/// checks (duplicate ValidFrom, ordering against other versions) live in the handler and are
/// covered by CreateProductWorkRateCommandHandlerTests instead.
/// </summary>
public class CreateProductWorkRateCommandValidatorTests
{
    private readonly CreateProductWorkRateCommandValidator _validator = new CreateProductWorkRateCommandValidator();

    private static CreateProductWorkRateCommand BuildValidCommand() => new CreateProductWorkRateCommand
    {
        ProductId = Guid.NewGuid(),
        WorkRateId = Guid.NewGuid(),
        AssemblyRatePerDay = 10,
        ValidFrom = new DateOnly(2026, 1, 1),
    };

    private static bool HasErrorFor(ValidationResult result, string propertyName) =>
        result.Errors.Exists(failure => failure.PropertyName == propertyName);

    [Fact]
    public void Validate_FullyPopulatedCommand_IsValid()
    {
        CreateProductWorkRateCommand command = BuildValidCommand();

        ValidationResult result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyProductId_IsInvalid()
    {
        CreateProductWorkRateCommand command = BuildValidCommand();
        command.ProductId = Guid.Empty;

        ValidationResult result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateProductWorkRateCommand.ProductId)).Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyWorkRateId_IsInvalid()
    {
        CreateProductWorkRateCommand command = BuildValidCommand();
        command.WorkRateId = Guid.Empty;

        ValidationResult result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateProductWorkRateCommand.WorkRateId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_AssemblyRatePerDayNotPositive_IsInvalid(int assemblyRatePerDay)
    {
        CreateProductWorkRateCommand command = BuildValidCommand();
        command.AssemblyRatePerDay = assemblyRatePerDay;

        ValidationResult result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateProductWorkRateCommand.AssemblyRatePerDay)).Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultValidFrom_IsInvalid()
    {
        CreateProductWorkRateCommand command = BuildValidCommand();
        command.ValidFrom = default;

        ValidationResult result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        HasErrorFor(result, nameof(CreateProductWorkRateCommand.ValidFrom)).Should().BeTrue();
    }
}
