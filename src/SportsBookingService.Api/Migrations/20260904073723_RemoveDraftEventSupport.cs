using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDraftEventSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationVotes");

            migrationBuilder.DropTable(
                name: "LocationOptions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Events",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "LocationOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationOptions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationVotes",
                columns: table => new
                {
                    LocationOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationVotes", x => new { x.LocationOptionId, x.MemberId });
                    table.ForeignKey(
                        name: "FK_LocationVotes_LocationOptions_LocationOptionId",
                        column: x => x.LocationOptionId,
                        principalTable: "LocationOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationVotes_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationOptions_EventId",
                table: "LocationOptions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationVotes_MemberId",
                table: "LocationVotes",
                column: "MemberId");
        }
    }
}
