using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linky.Migrations
{
    /// <inheritdoc />
    public partial class RemovedOneToOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Visitors_ActiveVisitors_SessionId",
                table: "Visitors");

            migrationBuilder.DropIndex(
                name: "IX_Visitors_SessionId",
                table: "Visitors");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "Visitors");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "Visitors",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_SessionId",
                table: "Visitors",
                column: "SessionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Visitors_ActiveVisitors_SessionId",
                table: "Visitors",
                column: "SessionId",
                principalTable: "ActiveVisitors",
                principalColumn: "SessionId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
