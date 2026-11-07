using System.Security.Claims;
using back.Models;
using back.Payments;
using back.Services.Pedidos;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;

namespace back.GraphQL;

// Producto y cantidad solicitada
[GraphQLName("ItemPedidoInput")]
public record ItemPedidoInput(
    int ProductoId,
    int Cantidad
);

// Pedido con pago mediante Mercado Pago
[GraphQLName("CrearPedidoInput")]
public record CrearPedidoInput(
    int UsuarioId,
    List<ItemPedidoInput> Items,
    long PaymentId,
    string NombreCompleto,
    string DireccionEntrega,
    string Telefono,
    double? Latitud,
    double? Longitud
);

// Pedido con pago fuera de línea
[GraphQLName("CrearPedidoSinPagoInput")]
public record CrearPedidoSinPagoInput(
    int UsuarioId,
    List<ItemPedidoInput> Items,
    string MetodoPago,
    string NombreCompleto,
    string DireccionEntrega,
    string Telefono,
    double? Latitud,
    double? Longitud
);

// Solo recibe, comprueba permisos y delega. La lógica vive en
// MercadoPagoPaymentService, PayPalPaymentService y PedidoService.
[ExtendObjectType(typeof(Mutation))]
public class PedidoMutation
{
    private static readonly string[] MetodosSinPago = { "Efectivo", "Transferencia" };

    // CREAR PEDIDO CON MERCADO PAGO
    [Authorize]
    public async Task<Pedido> CrearPedido(
        CrearPedidoInput input,
        ClaimsPrincipal user,
        [Service] MercadoPagoPaymentService payments)
    {
        PedidoAuth.ValidarPropietario(user, input.UsuarioId);

        try
        {
            return await payments.CrearPedidoPagadoAsync(
                input.UsuarioId,
                input.PaymentId,
                input.Items,
                input.NombreCompleto,
                input.DireccionEntrega,
                input.Telefono,
                input.Latitud,
                input.Longitud);
        }
        catch (PaymentRequestException ex)
        {
            // GraphQLException sí muestra el mensaje al cliente
            throw new GraphQLException(ex.Message);
        }
    }

    // CREAR PEDIDO SIN PAGO EN LÍNEA (efectivo / transferencia)
    [Authorize]
    public async Task<Pedido> CrearPedidoSinPago(
        CrearPedidoSinPagoInput input,
        ClaimsPrincipal user,
        [Service] PedidoService pedidos)
    {
        PedidoAuth.ValidarPropietario(user, input.UsuarioId);

        var metodoPago = (input.MetodoPago ?? "").Trim();
        if (!MetodosSinPago.Contains(metodoPago, StringComparer.OrdinalIgnoreCase))
            throw new GraphQLException("Método de pago no válido para este tipo de pedido.");

        try
        {
            return await pedidos.CrearAsync(new NuevoPedido(
                input.UsuarioId,
                input.Items,
                input.NombreCompleto,
                input.DireccionEntrega,
                input.Telefono,
                $"Pendiente de pago ({metodoPago})",
                input.Latitud,
                input.Longitud));
        }
        catch (PaymentRequestException ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }
}
