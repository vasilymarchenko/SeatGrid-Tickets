using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeatGrid.API.Migrations
{
    /// <inheritdoc />
    public partial class DDD_AddOrderIdToBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "Bookings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_OrderId",
                table: "Bookings",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_OrderId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Bookings");
        }
    }
}
