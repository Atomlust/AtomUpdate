namespace AtomUpdate.Core.Models;

public sealed class ErrorResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
