using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Application.Behaviours;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Behaviours;

/// <summary>
/// Unit tests for <see cref="ValidationBehaviour{TRequest,TResponse}"/>.
/// </summary>
public class ValidationBehaviourTests
{
    private sealed record TestRequest(string name) : IRequest<string>;

    [Fact]
    public async Task Handle_WhenNoValidators_CallsNext()
    {
        // Arrange
        ValidationBehaviour<TestRequest, string> behaviour = new (Enumerable.Empty<IValidator<TestRequest>>());

        bool nextCalled = false;
        RequestHandlerDelegate<string> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("ok");
        };

        // Act
        string result = await behaviour.Handle(new TestRequest("hello"), next, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_WhenValidationPasses_CallsNext()
    {
        // Arrange
        ValidationBehaviour<TestRequest, string> behaviour = new (new IValidator<TestRequest>[] { new AlwaysValidValidator() });

        bool nextCalled = false;
        RequestHandlerDelegate<string> next = () =>
        {
            nextCalled = true;
            return Task.FromResult("ok");
        };

        // Act
        string result = await behaviour.Handle(new TestRequest("valid-name"), next, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_WhenValidationFails_ThrowsValidationException()
    {
        // Arrange
        ValidationBehaviour<TestRequest, string> behaviour = new (new IValidator<TestRequest>[] { new AlwaysInvalidValidator() });

        RequestHandlerDelegate<string> next = () => Task.FromResult("should-not-reach");

        // Act
        Func<Task> act = () => behaviour.Handle(new TestRequest("x"), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Name is always invalid in this test.*");
    }

    [Fact]
    public async Task Handle_WhenValidationFailsWithEmptyName_ThrowsValidationException()
    {
        // Arrange
        ValidationBehaviour<TestRequest, string> behaviour = new (new IValidator<TestRequest>[] { new AlwaysValidValidator() });

        RequestHandlerDelegate<string> next = () => Task.FromResult("should-not-reach");

        // Act
        Func<Task> act = () => behaviour.Handle(new TestRequest(string.Empty), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class AlwaysValidValidator : AbstractValidator<TestRequest>
    {
        public AlwaysValidValidator()
        {
            RuleFor(r => r.name).NotEmpty();
        }
    }

    private sealed class AlwaysInvalidValidator : AbstractValidator<TestRequest>
    {
        public AlwaysInvalidValidator()
        {
            RuleFor(r => r.name)
                .Must(_ => false)
                .WithMessage("Name is always invalid in this test.");
        }
    }
}
