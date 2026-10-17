namespace DungeonForge.Api.Errors;

// Errors raised by the request pipeline rather than a handler. Their operation is `<Stage>:<endpoint name>`.
public static class PipelineErrors
{
    public static readonly ErrorSpec Unhandled = new(
        ErrorCodes.Unexpected.Unhandled, ErrorKind.Unexpected, "The server failed to process the request.");

    // Field "body" keeps the `errors` dictionary the Web client reads for a 400.
    public static readonly ErrorSpec Malformed = new(
        ErrorCodes.Request.Malformed, ErrorKind.Validation, "The request body is missing or is not valid JSON for this endpoint.",
        Field: "body");
}
