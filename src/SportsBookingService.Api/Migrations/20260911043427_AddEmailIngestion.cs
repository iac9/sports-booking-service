using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBookingService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InboundEmailToken",
                table: "Groups",
                type: "text",
                nullable: true);

            // Backfill before the unique index below is created, so existing groups get a
            // token rather than tripping the constraint on NULLs-turned-duplicates later.
            // 12 hex chars is a subset of the [a-z0-9] alphabet GroupService.GenerateInboundToken
            // uses, and seeding the hash with the row's own Id keeps every value distinct.
            migrationBuilder.Sql(
                """
                UPDATE "Groups"
                SET "InboundEmailToken" = substr(md5(random()::text || clock_timestamp()::text || "Id"::text), 1, 12)
                WHERE "InboundEmailToken" IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "PendingBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawEmailFrom = table.Column<string>(type: "text", nullable: false),
                    RawEmailSubject = table.Column<string>(type: "text", nullable: false),
                    RawEmailText = table.Column<string>(type: "text", nullable: false),
                    RawEmailHtml = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProviderHint = table.Column<string>(type: "text", nullable: true),
                    ExtractedVenueName = table.Column<string>(type: "text", nullable: true),
                    ExtractedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExtractedStartTime = table.Column<string>(type: "text", nullable: true),
                    ExtractedEndTime = table.Column<string>(type: "text", nullable: true),
                    ExtractedCost = table.Column<decimal>(type: "numeric", nullable: true),
                    ExtractedCurrency = table.Column<string>(type: "text", nullable: true),
                    ExtractionConfidence = table.Column<double>(type: "double precision", nullable: true),
                    ExtractedRawJson = table.Column<string>(type: "text", nullable: true),
                    ExtractionError = table.Column<string>(type: "text", nullable: true),
                    MatchedLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchedEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingBookings_Events_CreatedEventId",
                        column: x => x.CreatedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PendingBookings_Events_MatchedEventId",
                        column: x => x.MatchedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PendingBookings_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PendingBookings_Locations_MatchedLocationId",
                        column: x => x.MatchedLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PendingBookings_Members_ReviewedByMemberId",
                        column: x => x.ReviewedByMemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_InboundEmailToken",
                table: "Groups",
                column: "InboundEmailToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingBookings_CreatedEventId",
                table: "PendingBookings",
                column: "CreatedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingBookings_GroupId_Status",
                table: "PendingBookings",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingBookings_MatchedEventId",
                table: "PendingBookings",
                column: "MatchedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingBookings_MatchedLocationId",
                table: "PendingBookings",
                column: "MatchedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingBookings_ReviewedByMemberId",
                table: "PendingBookings",
                column: "ReviewedByMemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingBookings");

            migrationBuilder.DropIndex(
                name: "IX_Groups_InboundEmailToken",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "InboundEmailToken",
                table: "Groups");
        }
    }
}
