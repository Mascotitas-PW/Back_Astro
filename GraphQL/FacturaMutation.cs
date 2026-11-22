using System.Security.Claims;
using back.Models;
using back.Services.Auth;
using back.Services.Factura;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace back.GraphQL;

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

    // El cliente solicita la factura (CFDI) de un pedido entregado
    [Authorize]
    public async Task<Factura> SolicitarFactura(
        SolicitudFacturaInput input,
        ClaimsPrincipal user,
        [Service] AppDbContext context,
        [Service] FacturacionService facturacion,
        CancellationToken ct)
    {
        var uid = AuthHelper.ObtenerUsuarioId(user);

        var pedido = await context.Pedidos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == input.PedidoId, ct)
            ?? throw new GraphQLException("El pedido no existe.");

        if (AuthHelper.EsAdmin(user))
            throw new GraphQLException("Como administrador usa «Facturar venta».");
        if (pedido.UsuarioId != uid)
            throw new GraphQLException("Ese pedido no es tuyo.");

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

    // El administrador factura la venta de la empresa (comprobante de Mascotitas)
    [Authorize(Roles = new[] { "Admin" })]
    public async Task<Factura> RegistrarVenta(
        RegistroVentaInput input,
        ClaimsPrincipal user,
        [Service] FacturacionService facturacion,
        CancellationToken ct)
    {
        try
        {
            return await facturacion.EmitirVentaAsync(
                input.PedidoId, AuthHelper.ObtenerUsuarioId(user),
                input.Cliente, input.FormaPago, input.Observaciones, ct);
        }
        catch (FacturacionException ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }
}