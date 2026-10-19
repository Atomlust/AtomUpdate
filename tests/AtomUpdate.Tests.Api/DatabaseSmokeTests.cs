using AtomUpdate.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

[Collection("api")]
public class DatabaseSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Database_IsReachable_And_MigrationsApplied()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>();

        Assert.True(await db.Database.CanConnectAsync());
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));
    }
}
