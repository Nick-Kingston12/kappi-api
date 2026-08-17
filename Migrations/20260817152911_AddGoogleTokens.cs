using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KappiApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleAccessToken",
                table: "Salons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleRefreshToken",
                table: "Salons",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleAccessToken",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "GoogleRefreshToken",
                table: "Salons");
        }
    }
}
