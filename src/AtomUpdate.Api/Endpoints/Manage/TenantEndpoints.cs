using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Endpoints.Manage;

internal static class TenantEndpoints
{
    // 租户为平台级操作（不按 tenant_id 过滤）：仅平台管理员（M2 两级权限模型，docs/07 §4.2）
    internal static void Map(IEndpointRouteBuilder group)
    {
        var platform = group.MapGroup("").RequireAuthorization(AuthPolicies.PlatformAdmin);

        platform.MapGet("/tenants", ListAsync)
            .Produces<PagedResult<TenantDto>>();
        platform.MapPost("/tenants", CreateAsync)
            .Produces<TenantDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);
        platform.MapGet("/tenants/{id:long}", GetAsync)
            .Produces<TenantDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        platform.MapPatch("/tenants/{id:long}", UpdateAsync)
            .Produces<TenantDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(AtomUpdateDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = ManageHelpers.Pager(page, pageSize);
        var query = db.Tenants.AsNoTracking().OrderBy(t => t.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<TenantDto> { Items = items.Select(ToDto).ToList(), Total = total });
    }

    private static async Task<IResult> CreateAsync(TenantCreateRequest request,
        IValidator<TenantCreateRequest> validator, AtomUpdateDbContext db, HttpContext http, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        if (await db.Tenants.AnyAsync(t => t.Slug == request.Slug, ct))
        {
            return ManageHelpers.Conflict("租户标识（slug）已存在");
        }

        var tenant = new Tenant
        {
            Name = request.Name,
            Slug = request.Slug,
            Status = EntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        ManageHelpers.Audit(db, tenant.Id, ManageHelpers.ActorOf(http), "tenant.create", "Tenant", tenant.Id,
            new { slug = tenant.Slug });
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/manage/tenants/{tenant.Id}", ToDto(tenant));
    }

    private static async Task<IResult> GetAsync(long id, AtomUpdateDbContext db, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return tenant is null
            ? ManageHelpers.NotFound(ErrorCodes.TenantNotFound, "租户不存在")
            : Results.Ok(ToDto(tenant));
    }

    private static async Task<IResult> UpdateAsync(long id, TenantUpdateRequest request,
        IValidator<TenantUpdateRequest> validator, AtomUpdateDbContext db, HttpContext http, CancellationToken ct)
    {
        if (await ManageHelpers.ValidateAsync(request, validator, ct) is { } fail)
        {
            return fail;
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
        {
            return ManageHelpers.NotFound(ErrorCodes.TenantNotFound, "租户不存在");
        }

        if (request.Name is { } name)
        {
            tenant.Name = name;
        }

        if (request.Status is { } status)
        {
            tenant.Status = status;
        }

        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        ManageHelpers.Audit(db, tenant.Id, ManageHelpers.ActorOf(http), "tenant.update", "Tenant", tenant.Id);
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDto(tenant));
    }

    private static TenantDto ToDto(Tenant tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        Slug = tenant.Slug,
        Status = tenant.Status,
        CreatedAt = tenant.CreatedAt
    };
}
