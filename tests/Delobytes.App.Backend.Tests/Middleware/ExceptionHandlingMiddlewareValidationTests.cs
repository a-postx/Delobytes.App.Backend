using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Middleware;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Middleware;

/// <summary>
/// Covers the FluentValidation.ValidationException branch of ExceptionHandlingMiddleware.
///
/// ValidationBehaviour throws that exception for every request rejected by a validator. Before the
/// branch existed, it fell through to the default case and answered 500 common.unexpected_error
/// with no field information -- which is exactly the answer a user editing an SKU cannot act on.
/// These tests pin the 422 envelope and the camelCase field keys the frontend maps back onto the
/// form.
/// </summary>
public class ExceptionHandlingMiddlewareValidationTests
{
    private static ExceptionHandlingMiddleware BuildMiddleware(RequestDelegate next)
        => new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

    private static DefaultHttpContext BuildContext()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task Invoke_ValidationException_Returns422WithValidationFailedCode()
    {
        // Arrange
        ValidationException exception = new ValidationException(new List<ValidationFailure>
        {
            new ValidationFailure("Sku", "SKU не может быть пустым."),
        });

        ExceptionHandlingMiddleware middleware = BuildMiddleware(_ => throw exception);
        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.UnprocessableEntity);

        using JsonDocument doc = await ReadBodyAsync(context);
        doc.RootElement.GetProperty("code").GetString().Should().Be(ErrorCodes.Common.ValidationFailed.Value);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(422);
    }

    [Fact]
    public async Task Invoke_ValidationException_ExposesFieldErrorsUnderCamelCaseKeys()
    {
        // Arrange: FluentValidation reports the CLR property name; the client sends and reads
        // camelCase, so "Sku" has to arrive as "sku" or the form cannot attach the error.
        ValidationException exception = new ValidationException(new List<ValidationFailure>
        {
            new ValidationFailure("Sku", "SKU не может быть пустым."),
        });

        ExceptionHandlingMiddleware middleware = BuildMiddleware(_ => throw exception);
        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        using JsonDocument doc = await ReadBodyAsync(context);
        JsonElement errors = doc.RootElement.GetProperty("errors");

        errors.TryGetProperty("sku", out JsonElement skuErrors).Should().BeTrue();
        skuErrors.EnumerateArray().Select(e => e.GetString())
            .Should().Contain("SKU не может быть пустым.");
        errors.TryGetProperty("Sku", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ValidationException_MergesErrorsOfTheSameField()
    {
        // Arrange: a field can fail more than one rule; both messages must survive.
        ValidationException exception = new ValidationException(new List<ValidationFailure>
        {
            new ValidationFailure("Name", "Название не может быть пустым."),
            new ValidationFailure("Name", "Длина названия не должна превышать 200 символов."),
        });

        ExceptionHandlingMiddleware middleware = BuildMiddleware(_ => throw exception);
        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        using JsonDocument doc = await ReadBodyAsync(context);
        JsonElement nameErrors = doc.RootElement.GetProperty("errors").GetProperty("name");

        nameErrors.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Invoke_AppExceptionSkuConflict_Returns409WithDomainCode()
    {
        // Arrange: the duplicate-SKU path. It arrives as an AppException thrown by
        // UniqueConstraintTranslator via the repository's save path, so it must keep its own
        // domain code rather than be reported as a validation failure.
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new AppException(ErrorCodes.Catalog.ProductSkuConflict));
        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);

        using JsonDocument doc = await ReadBodyAsync(context);
        doc.RootElement.GetProperty("code").GetString().Should().Be("catalog.product.sku_conflict");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
    }
}
