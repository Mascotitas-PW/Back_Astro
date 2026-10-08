using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace back.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261007120000_AddPayPalCheckouts")]
public partial class AddPayPalCheckouts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PayPalCheckouts",
            columns: table => new
            {
                orderid = table.Column<string>(type: "text", nullable: false),
                referenceid = table.Column<string>(type: "text", nullable: false),
                usuarioid = table.Column<int>(type: "integer", nullable: false),
                itemsjson = table.Column<string>(type: "text", nullable: false),
                merchandiseamount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                shippingamount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                totalamount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                currency = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                captureid = table.Column<string>(type: "text", nullable: true),
                pedidoid = table.Column<int>(type: "integer", nullable: true),
                createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PayPalCheckouts", row => row.orderid));

        migrationBuilder.CreateIndex(
            name: "IX_PayPalCheckouts_usuarioid",
            table: "PayPalCheckouts",
            column: "usuarioid");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PayPalCheckouts");
    }
}