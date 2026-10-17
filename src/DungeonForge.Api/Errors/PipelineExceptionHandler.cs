using Microsoft.AspNetCore.Diagnostics;

namespace DungeonForge.Api.Errors;

// Turns exceptions that escape an endpoint into ProblemDetails with no exception text or stack trace, and logs each one
// once (the exception middleware logs nothing when a handler takes the exception).
public sealed partial class PipelineExceptionHandler(ILogger<PipelineExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        // The middleware clears HttpContext's endpoint before calling handlers; the feature still has it.
        var endpoint = http.Features.Get<IExceptionHandlerFeature>()?.Endpoint?.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName
            ?? "unknown";
        Error error;
        if (exception is BadHttpRequestException)
        {
            error = PipelineErrors.Malformed.Create($"BindRequest:{endpoint}");
            LogMalformed(error.Operation, exception);
        }
        else
        {
            error = PipelineErrors.Unhandled.Create($"Unhandled:{endpoint}");
            LogUnhandled(error.Operation, exception);
        }

        await ErrorMapping.ToProblem(error, http).ExecuteAsync(http);
        return true;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Unhandled exception in {Operation}.")]
    private partial void LogUnhandled(string operation, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Request body could not be bound in {Operation}.")]
    private partial void LogMalformed(string operation, Exception exception);
}
