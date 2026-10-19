using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Endpoints.Manage;

internal static class ProjectEndpoints
{
    internal static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/projects", ListAsync)
            .Produces<PagedResult<ProjectDto>>();
        group.MapPost("/projects", CreateAsync)
            .Produces<ProjectDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
        group.MapGet("/projects/{id:long}", GetAsync)
            .Produces<ProjectDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapPatch("/projects/{id:long}", UpdateAsync)
            .Produces<ProjectDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        group.MapDelete("/projects/{id:long}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(AtomUpdateDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.Projects.AsNoTracking().OrderBy(x => x.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<ProjectDto> { Items = items.Select(ToDto).ToList(), Total = total });
    }

    private static async Task<IResult> CreateAsync(ProjectCreateRequest request,
        IValidator<ProjectCreateRequest> validator, AtomUpdateDbContext db, TenantContext tenantContext,
        HttpContext http, IReadCache readCache, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        if (tenantContext.TenantId is not { } tenantId)
        {
            return ManageHelpers.TenantRequired();
        }

        if (await db.Projects.AnyAsync(x => x.Key == request.Key, ct))
        {
            return ManageHelpers.Conflict("项目标识（key）已存在");
        }

        var project = new Project
        {
            TenantId = tenantId,
            Key = request.Key,
            Name = request.Name,
            Description = request.Description,
            Status = EntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Projects.Add(project);
        ManageHelpers.Audit(db, tenantId, ManageHelpers.ActorOf(http), "project.create", "Project", project.Id,
            new { key = project.Key });
        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // 同 key 项目可能已产生匿名反查负缓存/歧义缓存，创建后即时失效

        return Results.Created($"/api/manage/projects/{project.Id}", ToDto(project));
    }

    private static async Task<IResult> GetAsync(long id, AtomUpdateDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return project is null
            ? ManageHelpers.NotFound(ErrorCodes.ProjectNotFound, "项目不存在")
            : Results.Ok(ToDto(project));
    }

    private static async Task<IResult> UpdateAsync(long id, ProjectUpdateRequest request,
        IValidator<ProjectUpdateRequest> validator, AtomUpdateDbContext db, HttpContext http,
        IReadCache readCache, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        var project = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (project is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ProjectNotFound, "项目不存在");
        }

        if (request.Name is { } name)
        {
            project.Name = name;
        }

        if (request.Description is { } description)
        {
            project.Description = description;
        }

        if (request.Status is { } status)
        {
            project.Status = status;
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        ManageHelpers.Audit(db, project.TenantId, ManageHelpers.ActorOf(http), "project.update", "Project", project.Id);
        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // 项目状态（Active/Disabled）参与解析谓词，变更后即时失效

        return Results.Ok(ToDto(project));
    }

    /// <summary>受限删除：仅当项目下无任何应用时可删（防止在线客户端更新链路断裂）；
    /// 级联清理项目级与应用级 API Key，并写审计日志。</summary>
    private static async Task<IResult> DeleteAsync(long id, AtomUpdateDbContext db, HttpContext http,
        IReadCache readCache, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (project is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.ProjectNotFound, "项目不存在");
        }

        var appCount = await db.Apps.CountAsync(a => a.ProjectId == project.Id, ct);
        if (appCount > 0)
        {
            return ManageHelpers.Conflict($"项目下还有 {appCount} 个应用，请先删除全部应用");
        }

        db.Projects.Remove(project);

        ManageHelpers.Audit(db, project.TenantId, ManageHelpers.ActorOf(http), "project.delete", "Project",
            project.Id);
        await db.SaveChangesAsync(ct);
        readCache.InvalidateAll(); // 解析缓存与概览统计即时失效

        return Results.NoContent();
    }

    private static ProjectDto ToDto(Project project) => new()
    {
        Id = project.Id,
        Key = project.Key,
        Name = project.Name,
        Description = project.Description,
        Status = project.Status,
        CreatedAt = project.CreatedAt
    };
}
