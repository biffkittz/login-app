using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoginApp.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroceryRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroceryRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ShopperId = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastCallEndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShoppingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastCallOutcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroceryRuns_AspNetUsers_ShopperId",
                        column: x => x.ShopperId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GroceryRunParticipants",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryRunParticipants", x => new { x.RunId, x.UserId });
                    table.ForeignKey(
                        name: "FK_GroceryRunParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroceryRunParticipants_GroceryRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "GroceryRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroceryRunParticipants_UserId",
                table: "GroceryRunParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryRuns_ShopperId",
                table: "GroceryRuns",
                column: "ShopperId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryRuns_Status",
                table: "GroceryRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroceryRunParticipants");

            migrationBuilder.DropTable(
                name: "GroceryRuns");
        }
    }
}
