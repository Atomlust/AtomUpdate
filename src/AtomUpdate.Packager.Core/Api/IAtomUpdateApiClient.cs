using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Api;

/// <summary>AtomUpdate 服务端发布 API 客户端（docs/05 §11）：JWT 登录会话鉴权。
/// 路由：/api/projects/{projectId}/apps/{appId}/releases（POST 创建幂等 / GET 列表 / publish / unpublish）
/// 与 /api/manage/projects/{projectId}/apps/{appId}/upload（multipart 包文件上传）。</summary>
public interface IAtomUpdateApiClient
{
    Task<ReleaseDto> CreateReleaseAsync(long projectId, long appId, ReleaseCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ReleaseDto> PublishReleaseAsync(long projectId, long appId, long releaseId,
        ReleasePublishRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<ReleaseDto>> ListReleasesAsync(long projectId, long appId, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>上传包文件到服务端存储（Manage 上传端点，仅 JWT 会话可用；同路径重复上传为覆盖写，重试幂等）。</summary>
    Task<UploadResult> UploadAssetAsync(long projectId, long appId, string version, string filePath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);
}
