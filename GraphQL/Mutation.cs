using Microsoft.EntityFrameworkCore;
using back.Models; 
using HotChocolate; 
using back;
using BCrypt.Net;

namespace back.GraphQL;

[GraphQLName("ItemPedidoInput")]
public record ItemPedidoInput(int ProductoId, int Cantidad);

[GraphQLName("CrearPedidoInput")]
public record CrearPedidoInput(
    int UsuarioId,
    List<ItemPedidoInput> Items,
    long? PaymentId = null,
    string? PayPalOrderId = null);

public class Mutation

{
    public async Task<string> Registrar(
    string nombre,
    string email,
    string password,
    string? rol,
    [Service] AppDbContext context)

{
    var emailLimpio = email.Trim().ToLower();

    var existe = await context.Usuarios.AnyAsync(u => u.email.ToLower() == emailLimpio);
    if (existe)
    {
        throw new GraphQLException(
            ErrorBuilder.New()
                .SetMessage("El correo electrónico ya está registrado.")
                .SetCode("EMAIL_DUPLICADO")
                .Build()
        );
    }


    var rolFinal = string.IsNullOrWhiteSpace(rol) ? "Cliente" : rol.Trim();
    

    var rolesPermitidos = new[] { "Admin", "Cliente" };
    if (!rolesPermitidos.Contains(rolFinal))
    {
        throw new GraphQLException("El rol especificado no es válido.");
    }

    string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

    var nuevoUsuario = new User
    {
        Nombre = nombre.Trim(),
        email = emailLimpio,
        Password = passwordHash, 
        Rol = rolFinal 
    };

    context.Usuarios.Add(nuevoUsuario);
    await context.SaveChangesAsync();

    return "Usuario registrado con éxito";
}

  public async Task<User> Login(string email, string password, [Service] AppDbContext context)
{
    var emailLimpio = email.Trim().ToLower();
    var usuario = await context.Usuarios.FirstOrDefaultAsync(u => u.email.ToLower() == emailLimpio);

    if (usuario != null && BCrypt.Net.BCrypt.Verify(password, usuario.Password))
        return usuario;

    throw new GraphQLException("Usuario o contraseña incorrectos.");
}

    public async Task<Pedido> CrearPedido(
        CrearPedidoInput input,
        [Service] AppDbContext context,
        [Service] MercadoPagoPaymentService payments,
        [Service] PayPalPaymentService paypalPayments,
        [Service] IHttpContextAccessor httpContextAccessor)
{
    if (input.Items == null || input.Items.Count == 0)
        throw new GraphQLException("El pedido no tiene productos.");

    if (input.Items.Any(i => i.Cantidad <= 0))
        throw new GraphQLException("Todas las cantidades deben ser mayores a 0.");

    // Validar usuario
    var usuarioExiste = await context.Usuarios.AnyAsync(u => u.Id == input.UsuarioId);
    if (!usuarioExiste)
        throw new GraphQLException($"El usuario con ID {input.UsuarioId} no existe.");

    // Agrupar items repetidos
    var items = input.Items
        .GroupBy(i => i.ProductoId)
        .Select(g => new ItemPedidoInput(g.Key, g.Sum(x => x.Cantidad)))
        .ToList();

    PayPalCheckout? paypalCheckout = null;
    Dictionary<int, PayPalCheckoutItem>? paypalItems = null;
    if (!string.IsNullOrWhiteSpace(input.PayPalOrderId))
    {
        if (!PayPalCheckoutEndpoints.TryGetAuthenticatedUserId(httpContextAccessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal(), out var authenticatedUserId) ||
            authenticatedUserId != input.UsuarioId)
        {
            throw new GraphQLException("Se requiere autenticación del usuario propietario del checkout PayPal.");
        }
        paypalCheckout = await paypalPayments.GetCapturedCheckoutAsync(input.PayPalOrderId, input.UsuarioId);
        if (paypalCheckout is null)
            throw new GraphQLException("El checkout PayPal no está capturado o no corresponde al usuario.");

        var capturedItems = System.Text.Json.JsonSerializer.Deserialize<List<PayPalCheckoutItem>>(paypalCheckout.ItemsJson) ?? [];
        paypalItems = capturedItems.ToDictionary(item => item.ProductoId);
        if (items.Count != paypalItems.Count ||
            items.Any(item => !paypalItems.TryGetValue(item.ProductoId, out var captured) || captured.Cantidad != item.Cantidad))
        {
            throw new GraphQLException("Los productos del pedido no coinciden con el checkout PayPal.");
        }
    }
    else if (input.PaymentId is null or <= 0)
    {
        throw new GraphQLException("Se requiere un pago de Mercado Pago o un paypalOrderId capturado.");
    }

    var ids = items.Select(i => i.ProductoId).ToList();
    var productos = await context.Productos
        .Where(p => ids.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id);

    var detalles = new List<DetallePedido>();
    decimal total = 0;
    
    foreach (var item in items)
    {
        if (!productos.TryGetValue(item.ProductoId, out var producto))
            throw new GraphQLException($"El producto {item.ProductoId} no existe.");

        if (producto.Stock < item.Cantidad)
            throw new GraphQLException($"Stock insuficiente para '{producto.Nombre}' (disponible: {producto.Stock}).");

        producto.Stock -= item.Cantidad;
        var precioUnitario = paypalItems is not null
            ? paypalItems[item.ProductoId].PrecioUnitario
            : producto.Precio;
        total += precioUnitario * item.Cantidad;

        detalles.Add(new DetallePedido
        {
            ProductoId = producto.Id,
            Cantidad = item.Cantidad,
            PrecioUnitario = (float)precioUnitario
        });
    }

    var pagoValido = paypalCheckout is not null
        ? paypalCheckout.MerchandiseAmount == decimal.Round(total, 2, MidpointRounding.AwayFromZero) &&
          paypalCheckout.TotalAmount == paypalCheckout.MerchandiseAmount + paypalCheckout.ShippingAmount
        : await payments.IsApprovedForOrderAsync(
            input.PaymentId!.Value,
            input.UsuarioId,
            items.ToDictionary(item => item.ProductoId, item => item.Cantidad),
            total);
    if (!pagoValido)
        throw new GraphQLException("El pago no está aprobado o no corresponde al importe del pedido.");

    await using var tx = await context.Database.BeginTransactionAsync();

    var pedido = new Pedido
    {
        
        Fecha = DateTime.UtcNow,
        Status = "Pendiente",
        UsuarioId = input.UsuarioId,
        Total = (float)(paypalCheckout?.TotalAmount ?? total)
    };

    context.Pedidos.Add(pedido);
    await context.SaveChangesAsync();

    foreach (var d in detalles)
    {
        d.PedidoId = pedido.Id;
    }

    context.DetallePedidos.AddRange(detalles);
    await context.SaveChangesAsync();

    if (paypalCheckout is not null)
    {
        paypalCheckout.PedidoId = pedido.Id;
        await context.SaveChangesAsync();
    }

    await tx.CommitAsync();
    return pedido;
}
}