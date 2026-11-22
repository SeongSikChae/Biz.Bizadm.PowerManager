using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Biz.Bizadm.PowerManager.Server.Spa.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Devices",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "Devices");
        }
    }
}
