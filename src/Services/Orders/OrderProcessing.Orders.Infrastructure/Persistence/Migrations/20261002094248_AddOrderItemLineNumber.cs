using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderProcessing.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemLineNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "line_number",
                table: "order_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "line_number",
                table: "order_items");
        }
    }
}
