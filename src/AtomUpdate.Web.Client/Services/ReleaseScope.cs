using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Web.Client.Services;

/// <summary>版本发布的定位作用域。release 端点按「项目/应用」分组
/// （/api/projects/{projectId}/apps/{appId}/releases）且没有按 id 单查的端点，
/// 所以 /releases/... 子页面必须携带 projectId/appId 查询参数，再拉列表按 id 命中。</summary>
public sealed record ReleaseScope(long ProjectId, long AppId)
{
    private const string ApiBase = "api/projects";

    public string CreateApiUrl => $"{ApiBase}/{ProjectId}/apps/{AppId}/releases";

    public string ListApiUrl => $"{CreateApiUrl}?page=1&pageSize=200";

    public string ApiUrl(long releaseId, string suffix = "") => $"{CreateApiUrl}/{releaseId}{suffix}";

    public string ListUrl => $"/releases?projectId={ProjectId}&appId={AppId}";

    public string CreateUrl() => $"/releases/new?projectId={ProjectId}&appId={AppId}";

    public string DetailUrl(long releaseId) => PageUrl(releaseId);

    public string EditUrl(long releaseId) => PageUrl(releaseId, "/edit");

    public string PublishUrl(long releaseId) => PageUrl(releaseId, "/publish");

    public string UnpublishUrl(long releaseId) => PageUrl(releaseId, "/unpublish");

    private string PageUrl(long releaseId, string suffix = "") =>
        $"/releases/{releaseId}{suffix}?projectId={ProjectId}&appId={AppId}";

    public static ReleaseScope? TryCreate(long? projectId, long? appId) =>
        projectId is { } pid && appId is { } aid ? new(pid, aid) : null;

    /// <summary>拉版本列表按 id 命中；失败原因（无权限/不存在/不属于该应用）以消息返回。</summary>
    public async Task<(ReleaseDto? Release, string? Error)> FindAsync(ApiClient api, long releaseId)
    {
        var result = await api.GetAsync<PagedResult<ReleaseDto>>(ListApiUrl);
        if (!result.IsSuccess)
        {
            return (null, result.ErrorMessage ?? "加载失败");
        }

        var release = result.Data?.Items.FirstOrDefault(r => r.Id == releaseId);
        return release is null ? (null, $"未找到版本 {releaseId}，或它不属于当前项目/应用") : (release, null);
    }

    /// <summary>平台是应用属性，版本子页面只读展示它，取不到时返回 null 由页面降级显示。</summary>
    public async Task<UpdatePlatform?> PlatformAsync(ApiClient api)
    {
        var result = await api.GetAsync<AppDto>($"{ApiRoutes.ManagePrefix}/apps/{AppId}");
        return result.IsSuccess ? result.Data?.Platform : null;
    }
}
