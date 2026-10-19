using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtomUpdate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckLogClientIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientIp",
                table: "UpdateCheckLogs",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientIp",
                table: "UpdateCheckLogs");
        }
    }
}
