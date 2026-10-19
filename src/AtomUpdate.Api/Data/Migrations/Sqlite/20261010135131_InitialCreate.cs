using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtomUpdate.Api.Data.SqliteMigrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    InstallationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FromVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ToVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DownloadedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstallationReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    InstallationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FromVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ToVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InstallMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Elevated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ElevationRejected = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ReportedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackageSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Bucket = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    VersionMajor = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionMinor = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionPatch = table.Column<int>(type: "INTEGER", nullable: false),
                    BuildNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ForceUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinSupportedVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UpdateCheckLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    InstallationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProjectKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AppKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Arch = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CurrentVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LatestVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    HasUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ETagHit = table.Column<bool>(type: "INTEGER", nullable: false),
                    PolicyUnchanged = table.Column<bool>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateCheckLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UpdatePolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    AllowForceUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowDowngrade = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    PollingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PollingIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    PollingJitterMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadOnStartup = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadOnPolling = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadOnManualCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadOnForceUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadOnNetworkRecovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadOnResume = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstallMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AllowPostpone = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxPostponeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowMeteredNetwork = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxDownloadSpeedKBps = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdatePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsPlatformAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<long>(type: "INTEGER", nullable: false),
                    InstallationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MachineFingerprintHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LastVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirstSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ReleaseAssets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReleaseId = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FromVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Arch = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PackageType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SilentArgs = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    InstallArgs = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SourceId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseAssets_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseChannels",
                columns: table => new
                {
                    ReleaseId = table.Column<long>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ReleaseSeq = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseChannels", x => new { x.ReleaseId, x.Channel });
                    table.ForeignKey(
                        name: "FK_ReleaseChannels_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseNotes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReleaseId = table.Column<long>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    New = table.Column<string>(type: "TEXT", nullable: false),
                    Improvements = table.Column<string>(type: "TEXT", nullable: false),
                    Fixes = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    RawMarkdown = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseNotes_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apps_ProjectId_AppId",
                table: "Apps",
                columns: new[] { "ProjectId", "AppId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_AppId_DownloadedAt",
                table: "DownloadLogs",
                columns: new[] { "AppId", "DownloadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_DownloadedAt_AppId",
                table: "DownloadLogs",
                columns: new[] { "DownloadedAt", "AppId" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_InstallationId",
                table: "DownloadLogs",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationReports_AppId_InstallationId_ToVersion_Stage_ErrorCode",
                table: "InstallationReports",
                columns: new[] { "AppId", "InstallationId", "ToVersion", "Stage", "ErrorCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_AppId_InstallationId",
                table: "Installations",
                columns: new[] { "AppId", "InstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_AppId_LastSeenAt",
                table: "Installations",
                columns: new[] { "AppId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_Key",
                table: "Projects",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAssets_ReleaseId_Kind_FromVersion_PackageType_Arch",
                table: "ReleaseAssets",
                columns: new[] { "ReleaseId", "Kind", "FromVersion", "PackageType", "Arch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseChannels_Channel_Status",
                table: "ReleaseChannels",
                columns: new[] { "Channel", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseNotes_ReleaseId_Language",
                table: "ReleaseNotes",
                columns: new[] { "ReleaseId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Status_VersionMajor_VersionMinor_VersionPatch",
                table: "Releases",
                columns: new[] { "AppId", "Status", "VersionMajor", "VersionMinor", "VersionPatch" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_AppId_Version",
                table: "Releases",
                columns: new[] { "AppId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UpdateCheckLogs_AppId_CheckedAt",
                table: "UpdateCheckLogs",
                columns: new[] { "AppId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UpdateCheckLogs_CheckedAt_AppId",
                table: "UpdateCheckLogs",
                columns: new[] { "CheckedAt", "AppId" });

            migrationBuilder.CreateIndex(
                name: "IX_UpdateCheckLogs_InstallationId",
                table: "UpdateCheckLogs",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_UpdatePolicies_AppId_Channel",
                table: "UpdatePolicies",
                columns: new[] { "AppId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true,
                filter: "\"UserName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DownloadLogs");

            migrationBuilder.DropTable(
                name: "InstallationReports");

            migrationBuilder.DropTable(
                name: "Installations");

            migrationBuilder.DropTable(
                name: "PackageSources");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "ReleaseAssets");

            migrationBuilder.DropTable(
                name: "ReleaseChannels");

            migrationBuilder.DropTable(
                name: "ReleaseNotes");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "UpdateCheckLogs");

            migrationBuilder.DropTable(
                name: "UpdatePolicies");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Apps");

            migrationBuilder.DropTable(
                name: "Releases");
        }
    }
}
