using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminMemberToGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdminMemberId",
                table: "Groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Groups" g SET "AdminMemberId" = (
                    SELECT m."Id" FROM "Members" m WHERE m."GroupId" = g."Id" ORDER BY m."Id" LIMIT 1
                );
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "AdminMemberId",
                table: "Groups",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid?),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_AdminMemberId",
                table: "Groups",
                column: "AdminMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Members_AdminMemberId",
                table: "Groups",
                column: "AdminMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Members_AdminMemberId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_AdminMemberId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "AdminMemberId",
                table: "Groups");
        }
    }
}
