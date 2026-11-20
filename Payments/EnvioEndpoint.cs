using System.Security.Claims;
using System.Text.Json;
using back.GraphQL;
using back.Models;
using Microsoft.EntityFrameworkCore;

namespace back.Payments;

public sealed record CrearCotizacionRequest(
    List<ItemPedidoInput>? Items,
    string? CodigoPostal,
    string? Estado,
    string? Ciudad,
    string? Colonia);

public sealed record CotizacionResponse(Guid Id, IReadOnlyList<TarifaEnvio> Tarifas, DateTime VenceUtc);

public static class EnvioEndpoint
{
    public static void MapEnvios(this WebApplication app)
    {
        app.MapPost("/api/envios/cotizaciones", async (
            CrearCotizacionRequest request,
            ClaimsPrincipal user,
            AppDbContext context,
            SkydropxClient skydropx,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId) || usuarioId < 1)
                return Results.Unauthorized();

            var cp = new string((request.CodigoPostal ?? "").Where(char.IsDigit).ToArray());
            var estado = (request.Estado ?? "").Trim();
            var ciudad = (request.Ciudad ?? "").Trim();
            var colonia = (request.Colonia ?? "").Trim();
            var items = request.Items?.GroupBy(item => item.ProductoId)
                .Select(group => new ItemPedidoInput(group.Key, group.Sum(item => item.Cantidad))).ToList();
            if (cp.Length != 5 || estado.Length < 2 || ciudad.Length < 2 || colonia.Length < 2 ||
                items is null || items.Count == 0 || items.Any(item => item.ProductoId < 1 || item.Cantidad < 1))
                return Results.BadRequest(new { message = "Completa el código postal, estado, ciudad, colonia y carrito." });

            var ids = items.Select(item => item.ProductoId).ToList();
            var productos = await context.Productos.Where(product => ids.Contains(product.Id)).ToDictionaryAsync(product => product.Id, cancellationToken);
            decimal pesoKg = configuration.GetValue<decimal>("Skydropx:Parcel:TareWeightKg");
            foreach (var item in items)
            {
                if (!productos.TryGetValue(item.ProductoId, out var producto))
                    return Results.BadRequest(new { message = $"El producto {item.ProductoId} no existe." });
                if (producto.PesoKg is null or <= 0)
                    return Results.BadRequest(new { message = $"Configura el peso de '{producto.Nombre}' antes de cotizar." });
                pesoKg += producto.PesoKg.Value * item.Cantidad;
            }

            if (pesoKg <= 0)
                return Results.BadRequest(new { message = "El peso del paquete debe ser mayor a cero." });
            pesoKg = Math.Max(1m, pesoKg);

            try
            {
                var resultado = await skydropx.CotizarAsync(
                    new DireccionCotizacion(cp, estado, ciudad, colonia), pesoKg, cancellationToken);
                var ahora = DateTime.UtcNow;
                var cotizacion = new CotizacionEnvio
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = usuarioId,
                    SkydropxId = resultado.Id,
                    CodigoPostal = cp,
                    Estado = estado,
                    Ciudad = ciudad,
                    Colonia = colonia,
                    TarifasJson = JsonSerializer.Serialize(resultado.Tarifas),
                    CreadaUtc = ahora,
                    VenceUtc = ahora.AddHours(24)
                };
                context.Add(cotizacion);
                await context.SaveChangesAsync(cancellationToken);

                return Results.Ok(new CotizacionResponse(cotizacion.Id, resultado.Tarifas, cotizacion.VenceUtc));
            }
            catch (TimeoutException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No se pudo conectar con Skydropx Sandbox.", statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization();
    }
}