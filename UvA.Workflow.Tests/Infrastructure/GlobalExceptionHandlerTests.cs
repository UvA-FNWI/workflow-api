using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Tests.Infrastructure;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public Task MigrationValidationException_ReturnsBadRequestWithMessage()
        => AssertResponse(
            new MigrationValidationException("MigrationPropertyOverlap", "Properties overlap"),
            StatusCodes.Status400BadRequest,
            "MigrationPropertyOverlap",
            "Properties overlap");

    [Fact]
    public Task MigrationNotFoundException_ReturnsNotFoundWithMessage()
        => AssertResponse(
            new MigrationNotFoundException("migration-id"),
            StatusCodes.Status404NotFound,
            "MigrationNotFound",
            "Migration 'migration-id' does not exist");

    [Fact]
    public Task InvalidMigrationStateException_ReturnsConflictWithMessage()
        => AssertResponse(
            new InvalidMigrationStateException("Migration cannot be finished"),
            StatusCodes.Status409Conflict,
            "InvalidMigrationState",
            "Migration cannot be finished");

    private static async Task AssertResponse(
        Exception exception,
        int expectedStatus,
        string expectedCode,
        string expectedMessage)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedCode, body.RootElement.GetProperty("error").GetString());
        Assert.Equal(expectedMessage, body.RootElement.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }
}