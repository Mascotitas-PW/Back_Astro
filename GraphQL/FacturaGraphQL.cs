using System.Security.Claims;
using back.Models;
using back.Services.Auth;
using back.Services.Factura;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace back.GraphQL;

[GraphQLName("DatosFiscalesInput")]
public record DatosFiscalesInput(
    int? Id,
    string Alias,
    string Rfc,
    string RazonSocial,
    string RegimenFiscal,
    string CodigoPostalFiscal,
    string? DomicilioFiscal,
    bool Predeterminado);

[GraphQLName("SolicitudFacturaInput")]
public record SolicitudFacturaInput(
    int PedidoId,
    // Si el cliente ya tiene sus datos guardados, solo manda el id;
    // si no, manda los campos sueltos
    int? DatosFiscalesId,
    string? Rfc,
    string? RazonSocial,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? DomicilioFiscal,
    string UsoCfdi);

[GraphQLName("RegistroVentaInput")]
public record RegistroVentaInput(
    int PedidoId,
    string? Cliente,
    string? FormaPago,
    string? Observaciones);

// ── QUERIES ─────────────────────────────────────────────────────────────────
[ExtendObjectType(typeof(Query))]
public class FacturaQueries
{
    private const string CAMPOS = "id folio serie estado uuid fechaEmision subtotal iva total rfcReceptor razonSocialReceptor usoCfdi";

    // Facturas del cliente (propias)
    [Authorize]
    public async Task<List<Factura>> GetMisFacturas(
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var id = AuthHelper.ObtenerUsuarioId(user);

        return await context.Facturas
            .AsNoTracking()
            .Where(f => f.UsuarioId == id)
            .OrderByDescending(f => f.FechaEmision)
            .Select(f => new Factura
            {
                Id = f.Id, PedidoId = f.PedidoId, UsuarioId = f.UsuarioId,
                Serie = f.Serie, Folio = f.Folio, Estado = f.Estado,
                Uuid = f.Uuid, FechaEmision = f.FechaEmision,
                Subtotal = f.Subtotal, Iva = f.Iva, Total = f.Total,
                RfcReceptor = f.RfcReceptor, RazonSocialReceptor = f.RazonSocialReceptor,
                UsoCfdi = f.UsoCfdi, TipoComprobante = f.TipoComprobante,
                Origen = f.Origen,
                MotivoError = f.MotivoError
                // Xml y Pdf se omiten en el listado (pesados); se bajan por el endpoint REST
            })
            .ToListAsync();
    }

    // Perfil(es) fiscal(es) guardados del cliente
    [Authorize]
    public async Task<List<DatosFiscalesCliente>> GetMisDatosFiscales(
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var id = AuthHelper.ObtenerUsuarioId(user);
        return await context.DatosFiscalesClientes
            .AsNoTracking()
            .Where(d => d.UsuarioId == id)
            .OrderByDescending(d => d.Predeterminado)
            .ToListAsync();
    }

    // Catálogos SAT para el formulario
    public Dictionary<string, string> GetCatalogoUsosCfdi() => CatalogosSat.UsosCfdi;
    public Dictionary<string, string> GetCatalogoRegimenesFiscales() => CatalogosSat.RegimenesFiscales;
}

