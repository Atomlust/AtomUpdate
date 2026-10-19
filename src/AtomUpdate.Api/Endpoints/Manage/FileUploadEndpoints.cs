using System.Security.Cryptography;
using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AtomUpdate.Api.Endpoints.Manage;

/// <summary>版本文件上传（存储路径：{root}/{appId}/{version}/{filename}）。</summary>
internal static class FileUploadEndpoints
{
    internal static void Map(this RouteGroupBuilder group)
    {
        group.MapPost("/projects/{projectId:long}/apps/{appId:long}/upload", UploadAsync)
            .DisableAntiforgery()
            .Produces<AssetUploadResult>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> UploadAsync(long projectId, long appId, string version,
        IFormFile file, AtomUpdateDbContext db, HttpContext http, IConfiguration config, CancellationToken ct)
    {
        if (file.Length is 0)
        {
            return Results.BadRequest(new ErrorResponse { Code = ErrorCodes.InvalidRequest, Message = "文件为空" });
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return Results.BadRequest(new ErrorResponse { Code = ErrorCodes.InvalidRequest, Message = "版本号必填" });
        }

        var app = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId && a.ProjectId == projectId, ct);
        if (app is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.AppNotFound, "应用不存在");
        }

        // 存储根目录（相对路径基于 ContentRoot 解析为绝对路径，Docker 中通过 Storage:Root 指定）
        var root = ManageHelpers.ResolveStorageRoot(config,
            http.RequestServices.GetRequiredService<IWebHostEnvironment>());
        var safeApp = app.AppId.Replace('/', '_').Replace('\\', '_');
        var dir = Path.Combine(root, safeApp, version);
        Directory.CreateDirectory(dir);

        var filename = Path.GetFileName(file.FileName);
        var dest = Path.Combine(dir, filename);

        // 写入磁盘 + 同步计算 SHA-256
        await using var stream = file.OpenReadStream();
        await using var fs = File.Create(dest);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        stream.Position = 0;
        await stream.CopyToAsync(fs, ct);
        await fs.FlushAsync(ct);

        var sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        var size = new FileInfo(dest).Length;

        // 构建可下载的 URL（同源 /uploads/...）
        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
        var url = $"{baseUrl}/uploads/{safeApp}/{version}/{Uri.EscapeDataString(filename)}";

        ManageHelpers.Audit(db, app.TenantId, ManageHelpers.ActorOf(http), "release.upload", "App", app.Id,
            new { appId = app.AppId, version, filename, size });
        await db.SaveChangesAsync(ct);

        return Results.Ok(new AssetUploadResult { Url = url, Sha256 = sha256, Size = size, Filename = filename });
    }
}
