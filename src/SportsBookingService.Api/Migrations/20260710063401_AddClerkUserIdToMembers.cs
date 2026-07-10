using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClerkUserIdToMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_GroupId",
                table: "Members");

            migrationBuilder.AddColumn<string>(
                name: "ClerkUserId",
                table: "Members",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_GroupId_ClerkUserId",
                table: "Members",
                columns: new[] { "GroupId", "ClerkUserId" },
                unique: true,
                filter: "\"ClerkUserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_GroupId_ClerkUserId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ClerkUserId",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "IX_Members_GroupId",
                table: "Members",
                column: "GroupId");
        }
    }
}
