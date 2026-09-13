using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingAutoAccept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAccepted",
                table: "PendingBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedBookedByName",
                table: "PendingBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MatchedPayerMemberId",
                table: "PendingBookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoAcceptBookings",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoAcceptMaxCost",
                table: "Groups",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingBookings_MatchedPayerMemberId",
                table: "PendingBookings",
                column: "MatchedPayerMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingBookings_Members_MatchedPayerMemberId",
                table: "PendingBookings",
                column: "MatchedPayerMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PendingBookings_Members_MatchedPayerMemberId",
                table: "PendingBookings");

            migrationBuilder.DropIndex(
                name: "IX_PendingBookings_MatchedPayerMemberId",
                table: "PendingBookings");

            migrationBuilder.DropColumn(
                name: "AutoAccepted",
                table: "PendingBookings");

            migrationBuilder.DropColumn(
                name: "ExtractedBookedByName",
                table: "PendingBookings");

            migrationBuilder.DropColumn(
                name: "MatchedPayerMemberId",
                table: "PendingBookings");

            migrationBuilder.DropColumn(
                name: "AutoAcceptBookings",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "AutoAcceptMaxCost",
                table: "Groups");
        }
    }
}
