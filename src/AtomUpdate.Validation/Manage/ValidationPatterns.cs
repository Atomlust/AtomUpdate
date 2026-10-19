namespace AtomUpdate.Validation.Manage;

internal static class ValidationPatterns
{
    /// <summary>小写字母/数字/连字符（tenant slug、project key、channel key、environment key）。</summary>
    internal const string Slug = "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$";

    /// <summary>应用业务标识（反向域名风格，允许点/下划线/连字符）。</summary>
    internal const string AppBusinessId = "^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$";
}
