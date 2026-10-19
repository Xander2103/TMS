using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace TransportationService.Api.Common.Diagnostics;

/// <summary>
/// LOG-01: ONE correlation id per request, used everywhere: the W3C trace id of the request's
/// Activity (falls back to the connection-level TraceIdentifier when no Activity exists). It is
/// returned in every error body (<c>correlationId</c>) and in the <c>X-Correlation-Id</c> response
/// header, and the logging scopes print the same TraceId on every log line of the request, so a
/// user-reported id leads straight to the server log.
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";

    public static string For(HttpContext context) =>
        Activity.Current?.TraceId is { } traceId && traceId != default
            ? traceId.ToHexString()
            : context.TraceIdentifier;
}

/// <summary>
/// Turns connection-level database failures into an honest 503 "Service unavailable" (with the
/// correlation id) instead of a generic 500, so clients, the SPA and the proxy can tell "try again
/// shortly" from a bug. Deliberately NARROW: only failures to reach/keep a database connection
/// qualify; constraint violations, SQL errors, a statement cancelled by the server (57014) and
/// every other exception keep their normal handling (500 or their own filter).
/// </summary>
public sealed class DatabaseUnavailableExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<DatabaseUnavailableExceptionHandler> _logger;

    public DatabaseUnavailableExceptionHandler(
        IProblemDetailsService problemDetails, ILogger<DatabaseUnavailableExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!IsDatabaseUnavailable(exception))
        {
            return false;
        }

        // Handled exceptions are no longer logged by the framework middleware (.NET 10), so the
        // outage is logged here, inside the request scope that carries the correlation id.
        _logger.LogError(exception, "Database unavailable while handling {Method} {Path}; answered 503.",
            httpContext.Request.Method, httpContext.Request.Path.Value);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "5";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Service unavailable",
            Detail = "De dienst is tijdelijk niet beschikbaar (database onbereikbaar). Probeer het over enkele "
                     + "ogenblikken opnieuw en vermeld de correlatie-id bij een melding.",
            Type = "https://httpstatuses.io/503",
        };
        problem.Extensions["code"] = ErrorCodes.ServiceUnavailable;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>
    /// True only for "cannot reach / lost the database": a Npgsql connection/IO failure (any
    /// <see cref="NpgsqlException"/> that is not a server-side <see cref="PostgresException"/>), a
    /// server-side connection-class error (SQLSTATE 08xxx, 57P01-57P03 shutdown/starting up, 53300
    /// too many connections), or EF's retry strategy giving up. Searched through the inner chain
    /// because EF wraps provider exceptions (DbUpdateException, InvalidOperationException).
    /// </summary>
    public static bool IsDatabaseUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case RetryLimitExceededException:
                    return true;
                case PostgresException postgres:
                    return IsConnectionSqlState(postgres.SqlState);
                case NpgsqlException:
                    return true;
            }
        }

        return false;
    }

    private static bool IsConnectionSqlState(string? sqlState) =>
        sqlState is not null
        && (sqlState.StartsWith("08", StringComparison.Ordinal)
            || sqlState is "57P01" or "57P02" or "57P03" or "53300");
}

/// <summary>
/// LOG-01: request logging plus the correlation-id response header. Logs method, path (never the
/// query string, which can carry tokens), status and duration: at Warning for 5xx, at Information
/// for 401/403/429 (auth and abuse signals), at Debug otherwise. The line is written inside the
/// hosting scope, so with console scopes enabled it carries the same TraceId as the error body.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationId.For(context);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Warning
                : status is 401 or 403 or 429 ? LogLevel.Information
                : LogLevel.Debug;
            if (_logger.IsEnabled(level))
            {
                _logger.Log(level, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms (correlationId {CorrelationId})",
                    context.Request.Method, context.Request.Path.Value, status,
                    (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, correlationId);
            }
        }
    }
}

public static class ApiDiagnosticsExtensions
{
    /// <summary>
    /// RFC 7807 errors without internals, carrying the correlation id; the narrow 503 mapping for
    /// database outages. Shared by Program.cs and the pipeline tests.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["correlationId"] = CorrelationId.For(context.HttpContext);
                context.ProblemDetails.Extensions.Remove("exception");

                if (context.ProblemDetails.Status is StatusCodes.Status500InternalServerError)
                {
                    context.ProblemDetails.Title = "Er is een onverwachte fout opgetreden.";
                    context.ProblemDetails.Detail =
                        "De aanvraag kon niet worden verwerkt. Vermeld de correlatie-id bij een melding.";
                }
            };
        });
        services.AddExceptionHandler<DatabaseUnavailableExceptionHandler>();
        return services;
    }

    /// <summary>
    /// Console logging with scopes: every line of a request carries SpanId/TraceId (the
    /// correlation id) and the request path. Single-line outside Development so journald keeps
    /// one entry per event and the existing <c>^(fail|crit):</c> greps keep working; a JSON
    /// formatter can still be chosen with <c>Logging:Console:FormatterName=json</c>.
    /// </summary>
    public static ILoggingBuilder AddOperationalConsoleLogging(this ILoggingBuilder logging, IHostEnvironment environment)
    {
        logging.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = !environment.IsDevelopment();
        });
        logging.Services.Configure<Microsoft.Extensions.Logging.Console.JsonConsoleFormatterOptions>(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
        });
        logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.SpanId | ActivityTrackingOptions.TraceId | ActivityTrackingOptions.ParentId);
        return logging;
    }
}
