using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestSupportToMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AddedByMemberId",
                table: "Members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "Members",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedByMemberId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "Members");
        }
    }
}
