using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Middleware;

/// <summary>
/// Tests for ExceptionHandlingMiddleware.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware BuildMiddleware(RequestDelegate next)
        => new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

    private static DefaultHttpContext BuildContext()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<(int StatusCode, string? Message, string? Code, int? BodyStatus)> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        JsonDocument doc = JsonDocument.Parse(body);

        string? message = doc.RootElement.TryGetProperty("message", out JsonElement messageProp)
            ? messageProp.GetString()
            : null;

        string? code = doc.RootElement.TryGetProperty("code", out JsonElement codeProp)
            ? codeProp.GetString()
            : null;

        int? bodyStatus = doc.RootElement.TryGetProperty("status", out JsonElement statusProp)
            ? statusProp.GetInt32()
            : null;

        return (context.Response.StatusCode, message, code, bodyStatus);
    }

    [Fact]
    public async Task Invoke_NoException_PassesThrough()
    {
        // Arrange
        bool nextCalled = false;
        ExceptionHandlingMiddleware middleware = BuildMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invoke_UnauthorizedAccessException_Returns401WithDefaultMessage()
    {
        // Arrange
        // exception.Message is intentionally not echoed — the DefaultMessage from ErrorCode is used instead.
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new UnauthorizedAccessException("Неверный адрес или пароль."));

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);
        (int statusCode, string? message, string? code, int? bodyStatus) = await ReadResponseAsync(context);

        // Assert
        statusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        message.Should().Be("Требуется аутентификация.");
        code.Should().Be("common.unauthorized");
        bodyStatus.Should().Be(401);
    }

    [Fact]
    public async Task Invoke_InvalidOperationException_Returns422WithDefaultMessage()
    {
        // Arrange
        // InvalidOperationException maps to ValidationFailed (422), not BadRequest (400).
        // exception.Message is not echoed — the DefaultMessage from ErrorCode is used instead.
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new InvalidOperationException("Пользователь с почтой test@example.com уже существует."));

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);
        (int statusCode, string? message, string? code, int? bodyStatus) = await ReadResponseAsync(context);

        // Assert
        statusCode.Should().Be(422);
        message.Should().Be("Проверьте корректность отправленных данных.");
        code.Should().Be("common.validation_failed");
        bodyStatus.Should().Be(422);
    }

    [Fact]
    public async Task Invoke_KeyNotFoundException_Returns404WithDefaultMessage()
    {
        // Arrange
        // exception.Message is not echoed — the DefaultMessage from ErrorCode is used instead.
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new KeyNotFoundException("Resource not found."));

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);
        (int statusCode, string? message, string? code, int? bodyStatus) = await ReadResponseAsync(context);

        // Assert
        statusCode.Should().Be((int)HttpStatusCode.NotFound);
        message.Should().Be("Ресурс не найден.");
        code.Should().Be("common.not_found");
        bodyStatus.Should().Be(404);
    }

    [Fact]
    public async Task Invoke_AppException_ReturnsCodeStatusAndMessage()
    {
        // Arrange
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new AppException(ErrorCodes.Catalog.ProductWorkRateNotFound));

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);
        (int statusCode, string? message, string? code, int? bodyStatus) = await ReadResponseAsync(context);

        // Assert
        statusCode.Should().Be((int)HttpStatusCode.NotFound);
        code.Should().Be("catalog.product_work_rate.not_found");
        message.Should().Be("Норма выработки не найдена.");
        bodyStatus.Should().Be(404);
    }

    [Fact]
    public async Task Invoke_AppException_WithCustomMessage_UsesCustomMessage()
    {
        // Arrange
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new AppException(ErrorCodes.Catalog.ProductWorkRateNotFound, "Норма выработки #42 не найдена."));

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);
        (int statusCode, string? message, string? code, int? bodyStatus) = await ReadResponseAsync(context);

        // Assert
        statusCode.Should().Be((int)HttpStatusCode.NotFound);
        code.Should().Be("catalog.product_work_rate.not_found");
        message.Should().Be("Норма выработки #42 не найдена.");
        bodyStatus.Should().Be(404);
    }

    [Fact]
    public async Task Invoke_UnknownException_Returns500WithGenericMessage()
    {
        // Arrange
#pragma warning disable CA2201 // Не порождайте исключения зарезервированных типов
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new Exception("Sensitive internal details."));
#pragma warning restore CA2201 // Не порождайте исключения зарезервированных типов

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);
        (int statusCode, string? message, string? code, int? bodyStatus) = await ReadResponseAsync(context);

        // Assert
        statusCode.Should().Be((int)HttpStatusCode.InternalServerError);

        // Internal details must not be leaked to the client
        message.Should().Be("An unexpected error occurred.");
        message.Should().NotContain("Sensitive internal details.");
        code.Should().Be("common.unexpected_error");
        bodyStatus.Should().Be(500);
    }

    [Fact]
    public async Task Invoke_ResponseBodyIsJson_ContentTypeIsApplicationJson()
    {
        // Arrange
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new InvalidOperationException("Some error."));

        DefaultHttpContext context = BuildContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task Invoke_AlwaysEmitsCorrelationHeader()
    {
        // Even without CorrelationIdMiddleware in the pipeline an error response must carry an
        // identifier the user can quote to support.
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new InvalidOperationException("Some error."));

        DefaultHttpContext context = BuildContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationHeaders.CorrelationId]
            .ToString().Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Invoke_WithIncomingCorrelationHeader_EchoesItOnErrorResponse()
    {
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new InvalidOperationException("Some error."));

        DefaultHttpContext context = BuildContext();
        context.Request.Headers[CorrelationHeaders.CorrelationId] = "user-quotable-id";

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationHeaders.CorrelationId]
            .ToString().Should().Be("user-quotable-id");
    }

    [Fact]
    public async Task Invoke_WithIncomingMalformedCorrelationHeader_SubstitutesIt()
    {
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new InvalidOperationException("Some error."));

        DefaultHttpContext context = BuildContext();
        context.Request.Headers[CorrelationHeaders.CorrelationId] = "bad value with spaces";

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationHeaders.CorrelationId]
            .ToString().Should().NotBe("bad value with spaces");
    }

    [Fact]
    public async Task Invoke_CorrelationHeaderAlreadySet_IsNotOverwritten()
    {
        // CorrelationIdMiddleware runs first and its value must win.
        ExceptionHandlingMiddleware middleware = BuildMiddleware(
            _ => throw new InvalidOperationException("Some error."));

        DefaultHttpContext context = BuildContext();
        context.Response.Headers[CorrelationHeaders.CorrelationId] = "set-upstream";

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationHeaders.CorrelationId]
            .ToString().Should().Be("set-upstream");
    }
}
