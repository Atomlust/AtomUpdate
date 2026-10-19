using AtomUpdate.Core.Models;

namespace AtomUpdate.Api.Auth;

internal static class AuthResponses
{
    // Results.Json 未显式传 options 时使用 DI 中的 ConfigureHttpJsonOptions（AtomUpdateJson），契约一致
    public static Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message) =>
        Results.Json(new ErrorResponse { Code = code, Message = message }, statusCode: statusCode)
            .ExecuteAsync(context);
}
