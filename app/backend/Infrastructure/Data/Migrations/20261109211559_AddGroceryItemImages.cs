using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoginApp.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroceryItemImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImageId",
                table: "GroceryItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImagePending",
                table: "GroceryItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GroceryItemImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NameKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryItemImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroceryListSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ImageGenerationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryListSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "GroceryListSettings",
                columns: new[] { "Id", "ImageGenerationEnabled", "Version" },
                values: new object[] { 1, true, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_GroceryItems_ImageId",
                table: "GroceryItems",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryItemImages_NameKey",
                table: "GroceryItemImages",
                column: "NameKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GroceryItems_GroceryItemImages_ImageId",
                table: "GroceryItems",
                column: "ImageId",
                principalTable: "GroceryItemImages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroceryItems_GroceryItemImages_ImageId",
                table: "GroceryItems");

            migrationBuilder.DropTable(
                name: "GroceryItemImages");

            migrationBuilder.DropTable(
                name: "GroceryListSettings");

            migrationBuilder.DropIndex(
                name: "IX_GroceryItems_ImageId",
                table: "GroceryItems");

            migrationBuilder.DropColumn(
                name: "ImageId",
                table: "GroceryItems");

            migrationBuilder.DropColumn(
                name: "ImagePending",
                table: "GroceryItems");
        }
    }
}
