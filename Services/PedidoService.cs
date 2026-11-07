using back.GraphQL;
using back.Models;
using back.Payments;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace back.Services.Pedidos;

public sealed record NuevoPedido(
    int UsuarioId,
    IReadOnlyList<ItemPedidoInput>? Items,
    string? NombreCompleto,
    string? DireccionEntrega,
    string? Telefono,
    string Status,
    double? Latitud = null,
    double? Longitud = null);

// Único lugar donde se crea un pedido (Mercado Pago, PayPal y pagos fuera de línea).
// Lanza PaymentRequestException ante datos inválidos; quien lo llame decide cómo mostrarlo.
public sealed class PedidoService(AppDbContext context, ITopicEventSender sender)
{
    // Total a cobrar (productos + envío), calculado siempre en el servidor.
    public async Task<decimal> CalcularTotalAsync(
        int usuarioId,
        IEnumerable<ItemPedidoInput>? items,
        CancellationToken ct = default)
    {
        var (_, _, total) = await PrepararAsync(usuarioId, items, ct);
        return total;
    }

    // verificarPago se ejecuta con el total ya calculado y ANTES de guardar nada:
    // Mercado Pago comprueba el pago; PayPal captura. Si lanza, no se crea el pedido.
    public async Task<Pedido> CrearAsync(
        NuevoPedido datos,
        Func<decimal, Task>? verificarPago = null,
        CancellationToken ct = default)
    {
        var (nombre, direccion, telefono) = ValidarEntrega(
            datos.NombreCompleto, datos.DireccionEntrega, datos.Telefono);

        var (items, productos, total) = await PrepararAsync(datos.UsuarioId, datos.Items, ct);

        if (verificarPago is not null)
            await verificarPago(total);

        var ubicacion = NormalizarUbicacion(datos.Latitud, datos.Longitud);

        await using var tx = await context.Database.BeginTransactionAsync(ct);
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
                Status = datos.Status,
                UsuarioId = datos.UsuarioId,
                NombreCompleto = nombre,
                DireccionEntrega = direccion,
                Telefono = telefono,
                Total = (float)total,
                Latitud = ubicacion.Lat,
                Longitud = ubicacion.Lng
            };

            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync(ct);

            foreach (var detalle in detalles)
                detalle.PedidoId = pedido.Id;

            context.DetallePedidos.AddRange(detalles);
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await PublicarAsync(productos.Values, pedido);
            return pedido;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<(List<ItemPedidoInput> Items, Dictionary<int, Producto> Productos, decimal Total)> PrepararAsync(
        int usuarioId,
        IEnumerable<ItemPedidoInput>? items,
        CancellationToken ct)
    {
        var normalizados = Normalizar(items);

        if (usuarioId < 1 || !await context.Usuarios.AnyAsync(u => u.Id == usuarioId, ct))
            throw new PaymentRequestException("El usuario indicado no existe.");

        var ids = normalizados.Select(i => i.ProductoId).ToList();
        var productos = await context.Productos
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        decimal subtotal = 0;
        foreach (var item in normalizados)
        {
            if (!productos.TryGetValue(item.ProductoId, out var producto))
                throw new PaymentRequestException($"El producto {item.ProductoId} no existe.");
            if (producto.Stock < item.Cantidad)
                throw new PaymentRequestException(
                    $"Stock insuficiente para '{producto.Nombre}' (disponible: {producto.Stock}).");

            subtotal += producto.Precio * item.Cantidad;
        }

        var total = decimal.Round(subtotal + Envio.Calcular(subtotal), 2, MidpointRounding.AwayFromZero);
        return (normalizados, productos, total);
    }

    // Solo guarda coordenadas válidas (ambas presentes y dentro de rango).
    private static (double? Lat, double? Lng) NormalizarUbicacion(double? lat, double? lng) =>
        lat is >= -90 and <= 90 && lng is >= -180 and <= 180
            ? (lat, lng)
            : (null, null);

    // Combina productos repetidos y rechaza cantidades inválidas.
    private static List<ItemPedidoInput> Normalizar(IEnumerable<ItemPedidoInput>? items)
    {
        var lista = items?.ToList();
        if (lista is null || lista.Count == 0)
            throw new PaymentRequestException("El pedido no tiene productos.");
        if (lista.Any(i => i.ProductoId < 1 || i.Cantidad < 1))
            throw new PaymentRequestException("Los productos y cantidades no son válidos.");

        return lista
            .GroupBy(i => i.ProductoId)
            .Select(g => new ItemPedidoInput(g.Key, g.Sum(x => x.Cantidad)))
            .ToList();
    }

    // Misma regla que el front: nombre, dirección y teléfono de 10 dígitos.
    private static (string Nombre, string Direccion, string Telefono) ValidarEntrega(
        string? nombreCompleto, string? direccionEntrega, string? telefono)
    {
        var nombre = (nombreCompleto ?? "").Trim();
        var direccion = (direccionEntrega ?? "").Trim();
        var tel = new string((telefono ?? "").Where(char.IsDigit).ToArray());

        if (nombre.Length < 3)
            throw new PaymentRequestException("Escribe tu nombre completo.");
        if (direccion.Length < 5)
            throw new PaymentRequestException("Escribe una dirección de entrega válida.");
        if (tel.Length != 10)
            throw new PaymentRequestException("El teléfono debe tener 10 dígitos.");

        return (nombre, direccion, tel);
    }

    // El pedido ya se guardó: un fallo en el tiempo real no debe romperlo.
    private async Task PublicarAsync(IEnumerable<Producto> productos, Pedido pedido)
    {
        try
        {
            foreach (var p in productos)
                await sender.SendAsync(Subscription.TopicStock, new StockActualizado(p.Id, p.Stock));

            await sender.SendAsync(Subscription.TopicPedido, new PedidoCreado(pedido.Id, pedido.UsuarioId));
        }
        catch
        {
            // El tiempo real es secundario.
        }
    }
}
