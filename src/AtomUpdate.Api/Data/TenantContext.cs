namespace AtomUpdate.Api.Data;

/// <summary>请求内租户上下文：由 JWT 声明中间件或公开端点的 projectKey 反查写入；未设置时租户过滤查询为空。</summary>
public sealed class TenantContext
{
    public long? TenantId { get; set; }
}
