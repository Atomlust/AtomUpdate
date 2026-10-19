using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtomUpdate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Releases_AppId_Status",
                table: "Releases");

            migrationBuilder.CreateIndex(
                name: "IX_UpdateCheckLogs_CheckedAt_AppId",
                table: "UpdateCheckLogs",
                columns: new[] { "CheckedAt", "AppId" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Status_VersionMajor_VersionMinor_VersionPatch",
                table: "Releases",
                columns: new[] { "AppId", "Status", "VersionMajor", "VersionMinor", "VersionPatch" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_DownloadedAt_AppId",
                table: "DownloadLogs",
                columns: new[] { "DownloadedAt", "AppId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UpdateCheckLogs_CheckedAt_AppId",
                table: "UpdateCheckLogs");

            migrationBuilder.DropIndex(
                name: "IX_Releases_AppId_Status_VersionMajor_VersionMinor_VersionPatch",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_DownloadLogs_DownloadedAt_AppId",
                table: "DownloadLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Status",
                table: "Releases",
                columns: new[] { "AppId", "Status" });
        }
    }
}
