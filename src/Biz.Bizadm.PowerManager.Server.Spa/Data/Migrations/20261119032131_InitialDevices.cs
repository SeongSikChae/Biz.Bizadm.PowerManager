using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Biz.Bizadm.PowerManager.Server.Spa.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Mac = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    Company = table.Column<string>(type: "TEXT", nullable: false),
                    MqttKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastRequestedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Mac);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
