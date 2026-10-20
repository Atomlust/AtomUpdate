using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtomUpdate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppPlatformAndAssetArch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 平台从版本上移到应用，架构从版本下移到资产。
            // 顺序必须是「先加列 + 回填，再删旧列」，反过来会丢掉回填来源。
            migrationBuilder.AddColumn<string>(
                name: "Arch",
                table: "ReleaseAssets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "ReleaseAssets" a
                SET "Arch" = r."Arch"
                FROM "Releases" r
                WHERE r."Id" = a."ReleaseId";
                """);

            migrationBuilder.Sql("""ALTER TABLE "ReleaseAssets" ALTER COLUMN "Arch" DROP DEFAULT""");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Apps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // 应用平台取该应用现有版本中最高的那个版本所标注的平台；没有版本的应用按 Windows
            migrationBuilder.Sql("""
                UPDATE "Apps" ap
                SET "Platform" = COALESCE(
                    (SELECT r."Platform"
                     FROM "Releases" r
                     WHERE r."AppId" = ap."Id"
                     ORDER BY r."VersionMajor" DESC, r."VersionMinor" DESC, r."VersionPatch" DESC, r."CreatedAt" DESC
                     LIMIT 1),
                    'Windows');
                """);

            migrationBuilder.Sql("""ALTER TABLE "Apps" ALTER COLUMN "Platform" DROP DEFAULT""");

            migrationBuilder.DropIndex(
                name: "IX_Releases_AppId_Platform_Arch_Status",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_Releases_AppId_Version_Platform_Arch",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_ReleaseAssets_ReleaseId_Kind_FromVersion_PackageType",
                table: "ReleaseAssets");

            migrationBuilder.DropColumn(
                name: "Arch",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Releases");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Status",
                table: "Releases",
                columns: new[] { "AppId", "Status" });

            // 唯一性回到 (AppId, Version)：若旧库里同一版本号按多个平台各存了一条，这里会直接报唯一冲突。
            // 不做静默合并——那种数据需要人工决定哪个平台留下（新模型下它属于不同的应用）。
            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Version",
                table: "Releases",
                columns: new[] { "AppId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAssets_ReleaseId_Kind_FromVersion_PackageType_Arch",
                table: "ReleaseAssets",
                columns: new[] { "ReleaseId", "Kind", "FromVersion", "PackageType", "Arch" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Releases_AppId_Status",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_Releases_AppId_Version",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_ReleaseAssets_ReleaseId_Kind_FromVersion_PackageType_Arch",
                table: "ReleaseAssets");

            migrationBuilder.AddColumn<string>(
                name: "Arch",
                table: "Releases",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Releases",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // 反向回填：平台来自应用，架构取该版本任一资产的架构（同版本多架构旧数据无法完整还原）
            migrationBuilder.Sql("""
                UPDATE "Releases" r
                SET "Platform" = ap."Platform",
                    "Arch" = COALESCE(
                        (SELECT a."Arch" FROM "ReleaseAssets" a WHERE a."ReleaseId" = r."Id" ORDER BY a."Id" LIMIT 1),
                        'X64')
                FROM "Apps" ap
                WHERE ap."Id" = r."AppId";
                """);

            migrationBuilder.DropColumn(
                name: "Arch",
                table: "ReleaseAssets");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Apps");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Platform_Arch_Status",
                table: "Releases",
                columns: new[] { "AppId", "Platform", "Arch", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Version_Platform_Arch",
                table: "Releases",
                columns: new[] { "AppId", "Version", "Platform", "Arch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAssets_ReleaseId_Kind_FromVersion_PackageType",
                table: "ReleaseAssets",
                columns: new[] { "ReleaseId", "Kind", "FromVersion", "PackageType" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
