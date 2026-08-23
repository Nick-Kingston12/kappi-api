using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KappiApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSalonSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Salons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoursText",
                table: "Salons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Salons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicesText",
                table: "Salons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamText",
                table: "Salons",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "HoursText",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "ServicesText",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "TeamText",
                table: "Salons");
        }
    }
}
