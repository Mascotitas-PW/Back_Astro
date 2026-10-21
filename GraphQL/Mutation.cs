
using Microsoft.EntityFrameworkCore;
using HotChocolate;
using HotChocolate.Authorization;
using System.Security.Claims;
using back.Models;
using back.Payments;
using HotChocolate.Subscriptions;
using back.Services.Auth;

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
    string Telefono
);

// Pedido con pago fuera de línea
[GraphQLName("CrearPedidoSinPagoInput")]
public record CrearPedidoSinPagoInput(
    int UsuarioId,
    List<ItemPedidoInput> Items,
    string MetodoPago,
    string NombreCompleto,
    string DireccionEntrega,
    string Telefono
);

public class Mutation
{
    // REGISTRAR USUARIO
    // IAuthService debe ignorar el rol enviado y crear un Cliente.
    public Task<string> Registrar(
        string nombre,
        string email,
        string password,
        string? rol,
        [Service] IAuthService auth)
    {
        return auth.RegistrarAsync(nombre, email, password);
    }

    // INICIAR SESIÓN
    public Task<AuthPayload> Login(
        string email,
        string password,
        [Service] IAuthService auth)
    {
        return auth.LoginAsync(email, password);
    }

    // CREAR PEDIDO CON MERCADO PAGO
    [Authorize]
    public async Task<Pedido> CrearPedido(
        CrearPedidoInput input,
        ClaimsPrincipal user,
        [Service] AppDbContext context,
        [Service] MercadoPagoPaymentService payments,
        [Service] ITopicEventSender sender)
    {
        // Validar propietario o administrador
        ValidarPropietario(user, input.UsuarioId);

        // Validar datos de entrega
        var (nombre, direccion, telefono) =
            ValidarDatosEntrega(
                input.NombreCompleto,
                input.DireccionEntrega,
                input.Telefono
            );

        // Validar productos solicitados
        var items = ValidarItems(input.Items);

        if (!await context.Usuarios.AnyAsync(
                u => u.Id == input.UsuarioId))
        {
            throw new GraphQLException(
                $"El usuario con ID {input.UsuarioId} no existe.");
        }

        var ids = items.Select(i => i.ProductoId).ToList();

        var productos = await context.Productos
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        decimal total = 0;

        foreach (var item in items)
        {
            if (!productos.TryGetValue(
                    item.ProductoId, out var producto))
            {
                throw new GraphQLException(
                    $"El producto {item.ProductoId} no existe.");
            }

            if (producto.Stock < item.Cantidad)
            {
                throw new GraphQLException(
                    $"Stock insuficiente para '{producto.Nombre}' " +
                    $"(disponible: {producto.Stock}).");
            }

            total += producto.Precio * item.Cantidad;
        }

        // Comprobar que el pago esté aprobado y corresponda
        // al usuario, productos, cantidades e importe del pedido.
        var pagoValido = await payments.IsApprovedForOrderAsync(
            input.PaymentId,
            input.UsuarioId,
            items.ToDictionary(
                item => item.ProductoId,
                item => item.Cantidad),
            total
        );

        if (!pagoValido)
        {
            throw new GraphQLException(
                "El pago no está aprobado o no corresponde " +
                "al importe del pedido.");
        }

        // Guardar pedido y descontar stock en una transacción.
        await using var tx =
            await context.Database.BeginTransactionAsync();

        try
        {
            var detalles = new List<DetallePedido>();

            foreach (var item in items)
            {
                var producto = productos[item.ProductoId];

                producto.Stock -= item.Cantidad;

                detalles.Add(new DetallePedido
                {
                    ProductoId = producto.Id,
                    Cantidad = item.Cantidad,
                    PrecioUnitario = (float)producto.Precio
                });
            }

            var pedido = new Pedido
            {
                Fecha = DateTime.UtcNow,
                Status = "Pendiente",
                UsuarioId = input.UsuarioId,
                NombreCompleto = nombre,
                DireccionEntrega = direccion,
                Telefono = telefono,
                Total = (float)total
            };

            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();

            foreach (var detalle in detalles)
            {
                detalle.PedidoId = pedido.Id;
            }

            context.DetallePedidos.AddRange(detalles);
            await context.SaveChangesAsync();

            await tx.CommitAsync();

            await PublicarStock(sender, productos.Values);

            return pedido;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // CREAR PEDIDO SIN PAGO EN LÍNEA
    [Authorize]
    public async Task<Pedido> CrearPedidoSinPago(
        CrearPedidoSinPagoInput input,
        ClaimsPrincipal user,
        [Service] AppDbContext context,
        [Service] ITopicEventSender sender)
    {
        // Validar propietario o administrador
        ValidarPropietario(user, input.UsuarioId);

        // Solo se permiten estos métodos
        var metodosPermitidos = new[]
        {
            "Efectivo",
            "Transferencia"
        };

        var metodoPago = (input.MetodoPago ?? "").Trim();

        if (!metodosPermitidos.Contains(
                metodoPago, StringComparer.OrdinalIgnoreCase))
        {
            throw new GraphQLException(
                "Método de pago no válido para este tipo de pedido.");
        }

        // Validar datos de entrega
        var (nombre, direccion, telefono) =
            ValidarDatosEntrega(
                input.NombreCompleto,
                input.DireccionEntrega,
                input.Telefono
            );

        // Validar productos
        var items = ValidarItems(input.Items);

        if (!await context.Usuarios.AnyAsync(
                u => u.Id == input.UsuarioId))
        {
            throw new GraphQLException(
                $"El usuario con ID {input.UsuarioId} no existe.");
        }

        var ids = items.Select(i => i.ProductoId).ToList();

        await using var tx =
            await context.Database.BeginTransactionAsync();

        try
        {
            var productos = await context.Productos
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            var detalles = new List<DetallePedido>();
            decimal subtotal = 0;

            foreach (var item in items)
            {
                if (!productos.TryGetValue(
                        item.ProductoId, out var producto))
                {
                    throw new GraphQLException(
                        $"El producto {item.ProductoId} no existe.");
                }

                if (producto.Stock < item.Cantidad)
                {
                    throw new GraphQLException(
                        $"Stock insuficiente para '{producto.Nombre}' " +
                        $"(disponible: {producto.Stock}).");
                }

                producto.Stock -= item.Cantidad;

                subtotal += producto.Precio * item.Cantidad;

                detalles.Add(new DetallePedido
                {
                    ProductoId = producto.Id,
                    Cantidad = item.Cantidad,
                    PrecioUnitario = (float)producto.Precio
                });
            }

            
            var envio = subtotal > 500 ? 0m : 99m;
            var total = subtotal + envio;

            var pedido = new Pedido
            {
                Fecha = DateTime.UtcNow,
                Status = $"Pendiente de pago ({metodoPago})",
                UsuarioId = input.UsuarioId,
                NombreCompleto = nombre,
                DireccionEntrega = direccion,
                Telefono = telefono,
                Total = (float)total
            };

            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();

            foreach (var detalle in detalles)
            {
                detalle.PedidoId = pedido.Id;
            }

            context.DetallePedidos.AddRange(detalles);
            await context.SaveChangesAsync();

            await tx.CommitAsync();

            await PublicarStock(sender, productos.Values);

            return pedido;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // MÉTODOS AUXILIARES

    // Avisa a los suscriptores (panel de admin, detalle de producto)
    // el stock nuevo de cada producto. Un fallo aquí no debe romper el pedido.
    private static async Task PublicarStock(
        ITopicEventSender sender,
        IEnumerable<Producto> productos)
    {
        try
        {
            foreach (var p in productos)
            {
                await sender.SendAsync(
                    Subscription.TopicStock,
                    new StockActualizado(p.Id, p.Stock));
            }
        }
        catch
        {
            // El pedido ya se guardó; el stock en vivo es secundario.
        }
    }

    private static void ValidarPropietario(
        ClaimsPrincipal user,
        int usuarioId)
    {
        if (usuarioId != AuthHelper.ObtenerUsuarioId(user)
            && !AuthHelper.EsAdmin(user))
        {
            throw new GraphQLException(
                "No puedes crear pedidos a nombre de otro usuario.");
        }
    }

    private static List<ItemPedidoInput> ValidarItems(
        List<ItemPedidoInput>? items)
    {
        if (items == null || items.Count == 0)
        {
            throw new GraphQLException(
                "El pedido no tiene productos.");
        }

        if (items.Any(i => i.Cantidad <= 0))
        {
            throw new GraphQLException(
                "Todas las cantidades deben ser mayores a 0.");
        }

        // Combinar productos repetidos.
        return items
            .GroupBy(i => i.ProductoId)
            .Select(g => new ItemPedidoInput(
                g.Key,
                g.Sum(x => x.Cantidad)))
            .ToList();
    }

    private static (
        string Nombre,
        string Direccion,
        string Telefono
    ) ValidarDatosEntrega(
        string? nombreCompleto,
        string? direccionEntrega,
        string? telefono)
    {
        var nombre = (nombreCompleto ?? "").Trim();
        var direccion = (direccionEntrega ?? "").Trim();

        var telefonoLimpio = new string(
            (telefono ?? "")
                .Where(char.IsDigit)
                .ToArray());

        if (nombre.Length < 3)
        {
            throw new GraphQLException(
                "Escribe tu nombre completo.");
        }

        if (direccion.Length < 5)
        {
            throw new GraphQLException(
                "Escribe una dirección de entrega válida.");
        }

        if (telefonoLimpio.Length != 10)
        {
            throw new GraphQLException(
                "El teléfono debe tener 10 dígitos.");
        }

        return (nombre, direccion, telefonoLimpio);
    }
}
