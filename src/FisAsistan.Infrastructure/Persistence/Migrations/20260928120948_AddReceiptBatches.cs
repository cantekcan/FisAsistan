using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FisAsistan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "Receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRegionJson",
                table: "Receipts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReceiptBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalStoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ImageWidth = table.Column<int>(type: "integer", nullable: false),
                    ImageHeight = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PendingRegionsJson = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptBatches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_BatchId",
                table: "Receipts",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptBatches_Status",
                table: "ReceiptBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptBatches_UserId",
                table: "ReceiptBatches",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Receipts_ReceiptBatches_BatchId",
                table: "Receipts",
                column: "BatchId",
                principalTable: "ReceiptBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_ReceiptBatches_BatchId",
                table: "Receipts");

            migrationBuilder.DropTable(
                name: "ReceiptBatches");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_BatchId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "SourceRegionJson",
                table: "Receipts");
        }
    }
}
