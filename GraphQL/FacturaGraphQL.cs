using System.Security.Claims;
using back.Models;
using back.Services.Auth;
using back.Services.Factura;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace back.GraphQL;

// ── INPUTS ──────────────────────────────────────────────────────────────────
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

// ── QUERIES ─────────────────────────────────────────────────────────────────
[ExtendObjectType(typeof(Query))]
public class FacturaQueries
{
    private const string CAMPOS = "id folio serie estado uuid fechaEmision subtotal iva total rfcReceptor razonSocialReceptor usoCfdi";

    // Facturas del cliente (propias o admin ve todas)
    [Authorize]
    public async Task<List<Factura>> GetMisFacturas(
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var id = AuthHelper.ObtenerUsuarioId(user);
        var esAdmin = AuthHelper.EsAdmin(user);

        return await context.Facturas
            .AsNoTracking()
            .Where(f => esAdmin || f.UsuarioId == id)
            .OrderByDescending(f => f.FechaEmision)
            .Select(f => new Factura
            {
                Id = f.Id, PedidoId = f.PedidoId, UsuarioId = f.UsuarioId,
                Serie = f.Serie, Folio = f.Folio, Estado = f.Estado,
                Uuid = f.Uuid, FechaEmision = f.FechaEmision,
                Subtotal = f.Subtotal, Iva = f.Iva, Total = f.Total,
                RfcReceptor = f.RfcReceptor, RazonSocialReceptor = f.RazonSocialReceptor,
                UsoCfdi = f.UsoCfdi, TipoComprobante = f.TipoComprobante,
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

// ── MUTATIONS ────────────────────────────────────────────────────────────────
[ExtendObjectType(typeof(Mutation))]
public class FacturaMutation
{
    // El cliente guarda / edita sus datos fiscales para no capturarlos cada vez
    [Authorize]
    public async Task<DatosFiscalesCliente> GuardarDatosFiscales(
        DatosFiscalesInput input,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var uid = AuthHelper.ObtenerUsuarioId(user);

        var rfc = input.Rfc.Trim().ToUpperInvariant();
        var rs = input.RazonSocial.Trim().ToUpperInvariant();
        var cp = input.CodigoPostalFiscal.Trim();

        if (rfc.Length is < 12 or > 13) throw new GraphQLException("RFC no válido.");
        if (rs.Length < 3) throw new GraphQLException("Escribe la razón social o nombre completo.");
        if (cp.Length != 5) throw new GraphQLException("El código postal fiscal debe tener 5 dígitos.");
        if (string.IsNullOrWhiteSpace(input.RegimenFiscal))
            throw new GraphQLException("Elige el régimen fiscal.");

        DatosFiscalesCliente datos;
        if (input.Id is int id)
        {
            datos = await context.DatosFiscalesClientes
                .FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == uid)
                ?? throw new GraphQLException("El perfil fiscal no existe.");
        }
        else
        {
            datos = new DatosFiscalesCliente { UsuarioId = uid };
            context.DatosFiscalesClientes.Add(datos);
        }

        datos.Alias = input.Alias.Trim();
        datos.Rfc = rfc;
        datos.RazonSocial = rs;
        datos.RegimenFiscal = input.RegimenFiscal;
        datos.CodigoPostalFiscal = cp;
        datos.DomicilioFiscal = string.IsNullOrWhiteSpace(input.DomicilioFiscal) ? null : input.DomicilioFiscal.Trim();
        datos.Predeterminado = input.Predeterminado;

        if (datos.Predeterminado)
        {
            var otros = await context.DatosFiscalesClientes
                .Where(d => d.UsuarioId == uid && d != datos && d.Predeterminado).ToListAsync();
            foreach (var o in otros) o.Predeterminado = false;
        }

        await context.SaveChangesAsync();
        return datos;
    }

    // Solicitar factura de un pedido entregado
    [Authorize]
    public async Task<Factura> SolicitarFactura(
        SolicitudFacturaInput input,
        ClaimsPrincipal user,
        [Service] AppDbContext context,
        [Service] FacturacionService facturacion,
        CancellationToken ct)
    {
        var uid = AuthHelper.ObtenerUsuarioId(user);

        // Verificar que el pedido es del usuario (o admin)
        var pedido = await context.Pedidos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == input.PedidoId, ct)
            ?? throw new GraphQLException("El pedido no existe.");
        AuthHelper.ExigirAdminOPropietario(user, pedido.UsuarioId);

        string rfc, rs, regimen, cp;
        string? domicilio;

        if (input.DatosFiscalesId is int dfId)
        {
            var df = await context.DatosFiscalesClientes
                .AsNoTracking().FirstOrDefaultAsync(d => d.Id == dfId && d.UsuarioId == uid, ct)
                ?? throw new GraphQLException("El perfil fiscal no existe.");
            rfc = df.Rfc; rs = df.RazonSocial; regimen = df.RegimenFiscal;
            cp = df.CodigoPostalFiscal; domicilio = df.DomicilioFiscal;
        }
        else
        {
            rfc = input.Rfc ?? throw new GraphQLException("Proporciona tu RFC.");
            rs = input.RazonSocial ?? throw new GraphQLException("Proporciona tu razón social.");
            regimen = input.RegimenFiscal ?? throw new GraphQLException("Elige tu régimen fiscal.");
            cp = input.CodigoPostalFiscal ?? throw new GraphQLException("Proporciona tu código postal fiscal.");
            domicilio = input.DomicilioFiscal;
        }

        try
        {
            return await facturacion.EmitirAsync(
                input.PedidoId, uid,
                rfc, rs, regimen, cp, domicilio, input.UsoCfdi, ct);
        }
        catch (FacturacionException ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }
}
