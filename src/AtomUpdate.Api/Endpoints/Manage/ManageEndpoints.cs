using AtomUpdate.Api.Auth;
using AtomUpdate.Core.Constants;

namespace AtomUpdate.Api.Endpoints.Manage;

internal static class ManageEndpoints
{
    internal static void MapManageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiRoutes.ManagePrefix)
            .RequireAuthorization(AuthPolicies.Manage)
            .WithTags("manage");

        TenantEndpoints.Map(group);
        ProjectEndpoints.Map(group);
        AppEndpoints.Map(group);
        UserEndpoints.Map(group);
        AuthEndpoints.Map(group);
        InsightEndpoints.Map(group);
        SettingsEndpoints.Map(group);
        FileUploadEndpoints.Map(group);
        ReleaseListEndpoint.Map(group);
    }
}
