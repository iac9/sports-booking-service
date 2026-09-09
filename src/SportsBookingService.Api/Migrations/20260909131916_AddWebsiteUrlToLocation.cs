using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteUrlToLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "Locations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "Locations");
        }
    }
}
