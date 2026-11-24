using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace back.Migrations
{
    /// <inheritdoc />
    public partial class AgregarIdClientePedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PesoKg",
                table: "Productos",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CiudadEntrega",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoPostalEntrega",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColoniaEntrega",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostoEnvio",
                table: "Pedido",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CotizacionEnvioId",
                table: "Pedido",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstadoEntrega",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NumeroPedidoUsuario",
                table: "Pedido",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Paqueteria",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicioEnvio",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkydropxRateId",
                table: "Pedido",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AceptacionLegal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AceptacionLegal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CotizacionEnvio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    SkydropxId = table.Column<string>(type: "text", nullable: false),
                    CodigoPostal = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Ciudad = table.Column<string>(type: "text", nullable: false),
                    Colonia = table.Column<string>(type: "text", nullable: false),
                    TarifasJson = table.Column<string>(type: "text", nullable: false),
                    CreadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotizacionEnvio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatosFiscalesCliente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    Rfc = table.Column<string>(type: "text", nullable: false),
                    RazonSocial = table.Column<string>(type: "text", nullable: false),
                    RegimenFiscal = table.Column<string>(type: "text", nullable: false),
                    CodigoPostalFiscal = table.Column<string>(type: "text", nullable: false),
                    DomicilioFiscal = table.Column<string>(type: "text", nullable: true),
                    Predeterminado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatosFiscalesCliente", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Domicilio",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    NombreCompleto = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false),
                    Direccion = table.Column<string>(type: "text", nullable: false),
                    CodigoPostal = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<string>(type: "text", nullable: true),
                    Ciudad = table.Column<string>(type: "text", nullable: true),
                    Colonia = table.Column<string>(type: "text", nullable: true),
                    Latitud = table.Column<double>(type: "double precision", nullable: true),
                    Longitud = table.Column<double>(type: "double precision", nullable: true),
                    Predeterminado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domicilio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Factura",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PedidoId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    NumeroPedido = table.Column<int>(type: "integer", nullable: false),
                    TipoComprobante = table.Column<string>(type: "text", nullable: false),
                    Serie = table.Column<string>(type: "text", nullable: false),
                    Folio = table.Column<int>(type: "integer", nullable: false),
                    RfcReceptor = table.Column<string>(type: "text", nullable: false),
                    RazonSocialReceptor = table.Column<string>(type: "text", nullable: false),
                    RegimenFiscalReceptor = table.Column<string>(type: "text", nullable: false),
                    CodigoPostalReceptor = table.Column<string>(type: "text", nullable: false),
                    DomicilioFiscalReceptor = table.Column<string>(type: "text", nullable: true),
                    UsoCfdi = table.Column<string>(type: "text", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    Iva = table.Column<decimal>(type: "numeric", nullable: false),
                    Total = table.Column<decimal>(type: "numeric", nullable: false),
                    FormaPago = table.Column<string>(type: "text", nullable: false),
                    MetodoPago = table.Column<string>(type: "text", nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Uuid = table.Column<string>(type: "text", nullable: true),
                    FacturamaId = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<byte[]>(type: "bytea", nullable: true),
                    Pdf = table.Column<byte[]>(type: "bytea", nullable: true),
                    Origen = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    MotivoError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factura", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Productos",
                keyColumn: "Id",
                keyValue: 1,
                column: "PesoKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Productos",
                keyColumn: "Id",
                keyValue: 2,
                column: "PesoKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Productos",
                keyColumn: "Id",
                keyValue: 3,
                column: "PesoKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Productos",
                keyColumn: "Id",
                keyValue: 4,
                column: "PesoKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Productos",
                keyColumn: "Id",
                keyValue: 5,
                column: "PesoKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Productos",
                keyColumn: "Id",
                keyValue: 6,
                column: "PesoKg",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AceptacionLegal");

            migrationBuilder.DropTable(
                name: "CotizacionEnvio");

            migrationBuilder.DropTable(
                name: "DatosFiscalesCliente");

            migrationBuilder.DropTable(
                name: "Domicilio");

            migrationBuilder.DropTable(
                name: "Factura");

            migrationBuilder.DropColumn(
                name: "PesoKg",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "CiudadEntrega",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "CodigoPostalEntrega",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "ColoniaEntrega",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "CostoEnvio",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "CotizacionEnvioId",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "EstadoEntrega",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "NumeroPedidoUsuario",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "Paqueteria",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "ServicioEnvio",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "SkydropxRateId",
                table: "Pedido");
        }
    }
}
