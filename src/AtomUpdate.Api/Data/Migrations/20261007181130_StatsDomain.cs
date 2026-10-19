using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtomUpdate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StatsDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AppId = table.Column<long>(type: "bigint", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ToVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstallationReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AppId = table.Column<long>(type: "bigint", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ToVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InstallMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Elevated = table.Column<bool>(type: "boolean", nullable: false),
                    ElevationRejected = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AppId = table.Column<long>(type: "bigint", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MachineFingerprintHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LastVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installations_Apps_AppId",
                        column: x => x.AppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_AppId_DownloadedAt",
                table: "DownloadLogs",
                columns: new[] { "AppId", "DownloadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_InstallationId",
                table: "DownloadLogs",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationReports_AppId_InstallationId_ToVersion_Stage_Er~",
                table: "InstallationReports",
                columns: new[] { "AppId", "InstallationId", "ToVersion", "Stage", "ErrorCode" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_AppId_InstallationId",
                table: "Installations",
                columns: new[] { "AppId", "InstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_AppId_LastSeenAt",
                table: "Installations",
                columns: new[] { "AppId", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadLogs");

            migrationBuilder.DropTable(
                name: "InstallationReports");

            migrationBuilder.DropTable(
                name: "Installations");
        }
    }
}
