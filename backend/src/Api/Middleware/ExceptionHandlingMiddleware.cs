using Domain.Exceptions;
using FluentValidation;
using Shared.Models;
using System.Net;
using System.Text.Json;

namespace Api.Middleware;

/// <summary>
/// Global exception handling middleware.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Initializes the middleware.
    /// </summary>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Handles the HTTP request pipeline.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client closed tab / navigated away — not a server fault (e.g. category tree abort after Save Draft).
            _logger.LogDebug(
                "Request aborted by client: {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Response may already be gone if the client disconnected mid-write.
        if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted)
        {
            _logger.LogDebug(exception, "Skipping error response — request aborted or response started.");
            return;
        }

        var (statusCode, message, errors) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                "Dữ liệu không hợp lệ.",
                validationEx.Errors.Select(e => e.ErrorMessage).ToList() as IReadOnlyList<string>),
            UnauthorizedException unauthorizedEx => (
                HttpStatusCode.Unauthorized,
                unauthorizedEx.Message,
                Array.Empty<string>() as IReadOnlyList<string>),
            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                notFoundEx.Message,
                Array.Empty<string>() as IReadOnlyList<string>),
            BusinessException businessEx => (
                HttpStatusCode.BadRequest,
                businessEx.Message,
                businessEx.Errors.Count > 0
                    ? businessEx.Errors
                    : Array.Empty<string>() as IReadOnlyList<string>),
            ConflictException conflictEx => (
                HttpStatusCode.Conflict,
                conflictEx.Message,
                conflictEx.Errors.Count > 0
                    ? conflictEx.Errors
                    : Array.Empty<string>() as IReadOnlyList<string>),
            UnauthorizedAccessException unauthorizedAccessEx => (
                HttpStatusCode.Unauthorized,
                unauthorizedAccessEx.Message,
                Array.Empty<string>() as IReadOnlyList<string>),
            OperationCanceledException => (
                HttpStatusCode.BadRequest,
                "Yêu cầu đã bị hủy.",
                Array.Empty<string>() as IReadOnlyList<string>),
            _ => (
                HttpStatusCode.InternalServerError,
                "Đã xảy ra lỗi hệ thống.",
                Array.Empty<string>() as IReadOnlyList<string>)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred.");
        }
        else
        {
            _logger.LogWarning(exception, "Request failed with {StatusCode}: {Message}", (int)statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse<object>.Fail(message, errors);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
