using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KappiApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationBufferReschedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Bookings");
        }
    }
}
