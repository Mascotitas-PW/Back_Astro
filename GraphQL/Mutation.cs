using Microsoft.EntityFrameworkCore;
using back.Models; 
using HotChocolate; 
using back;
using BCrypt.Net;

namespace back.GraphQL;

[GraphQLName("ItemPedidoInput")]
public record ItemPedidoInput(int ProductoId, int Cantidad);

[GraphQLName("CrearPedidoInput")]
public record CrearPedidoInput(int UsuarioId, List<ItemPedidoInput> Items);

public class Mutation
{
    public async Task<string> Registrar(
        string nombre,
        string email,
        string password,
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

      string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

    var nuevoUsuario = new User
    {
        Nombre = nombre.Trim(),
        email = emailLimpio,
        Password = passwordHash, 
        Rol = "Cliente"
    };
        context.Usuarios.Add(nuevoUsuario);
        await context.SaveChangesAsync();

        return "Usuario registrado con éxito";
    }

    public async Task<User> Login(string email, string password, [Service] AppDbContext context)
{

    var usuario = await context.Usuarios.FirstOrDefaultAsync(u => u.email == email);

    if (usuario != null && BCrypt.Net.BCrypt.Verify(password, usuario.Password))
    {
        return usuario;
    }

    throw new GraphQLException("Usuario o contraseña incorrectos.");
}

    public async Task<Pedido> CrearPedido(CrearPedidoInput input, [Service] AppDbContext context)
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

    var ids = items.Select(i => i.ProductoId).ToList();
    var productos = await context.Productos
        .Where(p => ids.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id);

    var detalles = new List<DetallePedido>();
    
    foreach (var item in items)
    {
        if (!productos.TryGetValue(item.ProductoId, out var producto))
            throw new GraphQLException($"El producto {item.ProductoId} no existe.");

        if (producto.Stock < item.Cantidad)
            throw new GraphQLException($"Stock insuficiente para '{producto.Nombre}' (disponible: {producto.Stock}).");

        producto.Stock -= item.Cantidad;

        detalles.Add(new DetallePedido
        {
            ProductoId = producto.Id,
            Cantidad = item.Cantidad,
            PrecioUnitario = (float)producto.Precio
        });
    }

    await using var tx = await context.Database.BeginTransactionAsync();

    var pedido = new Pedido
    {
        // Solución a la fecha en PostgreSQL
        Fecha = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        Status = "Pendiente",
        UsuarioId = input.UsuarioId,
        Total = (float)detalles.Sum(d => (decimal)d.PrecioUnitario * d.Cantidad)
    };

    context.Pedidos.Add(pedido);
    await context.SaveChangesAsync();

    foreach (var d in detalles)
    {
        d.PedidoId = pedido.Id;
    }

    context.DetallePedidos.AddRange(detalles);
    await context.SaveChangesAsync();

    await tx.CommitAsync();
    return pedido;
}
}