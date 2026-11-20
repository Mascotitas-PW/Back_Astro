using System.Data.Common;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using back.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace back.Payments;

public static class EnvioEndpoint
{
    public static void MapEnvios(this WebApplication app)
    {
        app.MapPost("/api/envios/cotizaciones", CotizarAsync).RequireAuthorization();
    }

    internal static async Task<IResult> CotizarAsync(
        CrearCotizacionRequest? request,
        ClaimsPrincipal user,
        AppDbContext context,
        ISkydropxClient skydropx,
        IConfiguration configuration,
        IMexicoPostalService postalService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("back.Envios");

        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId) || usuarioId < 1)
            return Results.Unauthorized();

        var (datos, error) = CotizacionValidation.Validar(request);
        if (datos is null)
            return Results.BadRequest(new { message = error });

        try
        {
            var codigoPostal = await postalService.BuscarAsync(datos.CodigoPostal, cancellationToken);
            if (codigoPostal is null)
                return Results.NotFound(new { message = "Código postal no encontrado." });

            var ids = datos.Items.Select(item => item.ProductoId).ToList();
            var productos = await context.Productos
                .Where(product => ids.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, cancellationToken);

            decimal pesoKg = SkydropxSettings.ReadDecimal(configuration, "Skydropx:Parcel:TareWeightKg", required: false);
            foreach (var item in datos.Items)
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

            var resultado = await skydropx.CotizarAsync(
                new DireccionCotizacion(datos.CodigoPostal, datos.Estado, datos.Ciudad, datos.Colonia),
                pesoKg,
                cancellationToken);

            var ahora = DateTime.UtcNow;
            var cotizacion = new CotizacionEnvio
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                SkydropxId = resultado.Id,
                CodigoPostal = datos.CodigoPostal,
                Estado = datos.Estado,
                Ciudad = datos.Ciudad,
                Colonia = datos.Colonia,
                TarifasJson = JsonSerializer.Serialize(resultado.Tarifas),
                CreadaUtc = ahora,
                VenceUtc = ahora.AddHours(24)
            };
            context.Add(cotizacion);
            await context.SaveChangesAsync(cancellationToken);

            return Results.Ok(new CotizacionResponse(cotizacion.Id, resultado.Tarifas, cotizacion.VenceUtc));
        }
        // El cliente cerró la conexión: no es un error del servidor.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (SkydropxConfigurationException ex)
        {
            logger.LogError(ex, "Configuración de Skydropx incompleta o inválida.");
            return Results.Json(new { message = "El servicio de envíos no está disponible por el momento." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (SkydropxProviderException ex)
        {
            // ex.Message lo redactamos nosotros en SkydropxClient (nunca es el cuerpo crudo del proveedor).
            logger.LogWarning(ex, "Skydropx devolvió un error ({Status}).", ex.StatusCode);
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
        }
        catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException)
        {
            logger.LogWarning(ex, "Tiempo de espera agotado al cotizar el envío.");
            return Results.Json(new { message = "El proveedor de envíos tardó demasiado en responder. Intenta de nuevo en unos segundos." },
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No se pudo conectar con un servicio externo al cotizar el envío.");
            return Results.Json(new { message = "No se pudo conectar con el proveedor de envíos." },
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Respuesta inválida de un servicio externo al cotizar el envío.");
            return Results.Json(new { message = "El proveedor de envíos devolvió una respuesta inválida." },
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception ex) when (EsErrorTransitorioDeBaseDeDatos(ex))
        {
            logger.LogError(ex, "La base de datos no estuvo disponible al cotizar el envío.");
            return Results.Json(new { message = "El servicio no está disponible temporalmente. Intenta de nuevo en unos minutos." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        // Cualquier otra excepción es inesperada: se propaga al handler global (ApiErrorHandling),
        // que la registra completa, responde 500 genérico y conserva los headers CORS.
    }

    private static bool EsErrorTransitorioDeBaseDeDatos(Exception ex)
    {
        var db = ex as DbException ?? (ex as DbUpdateException)?.InnerException as DbException;
        return db is NpgsqlException { IsTransient: true } ||
               db?.InnerException is TimeoutException or IOException or SocketException;
    }
}
