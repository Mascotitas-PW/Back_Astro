using Microsoft.EntityFrameworkCore;
using back.Models; 
using back.GraphQL; 
using HotChocolate; 
using back.Models;

namespace back.GraphQL;
using Microsoft.EntityFrameworkCore;
using HotChocolate;
using back.Models;


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

        var nuevoUsuario = new User
        {
            Nombre = nombre.Trim(),
            email = emailLimpio,
            Password = password,
            Rol = "Cliente"
        };

        context.Usuarios.Add(nuevoUsuario);
        await context.SaveChangesAsync();

        return "Usuario registrado con éxito";
    }

    public async Task<User?> Login(
        string email,
        string password,
        [Service] AppDbContext context)
    {
        return await context.Usuarios
            .FirstOrDefaultAsync(u =>
                u.email == email &&
                u.Password == password
            );
    }

    public async Task<Pedido> CrearPedido(CrearPedidoInput input, [Service] AppDbContext context)
    {
        if (input.Items == null || input.Items.Count == 0)
            throw new GraphQLException("El pedido no tiene productos.");

        if (input.Items.Any(i => i.Cantidad <= 0))
            throw new GraphQLException("Todas las cantidades deben ser mayores a 0.");

        if (!await context.Usuarios.AnyAsync(u => u.Id == input.UsuarioId))
            throw new GraphQLException("El usuario no existe.");

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
            Fecha = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), // columna timestamp sin zona
            Status = "Pendiente",
            UsuarioId = input.UsuarioId,
            Total = detalles.Sum(d => d.PrecioUnitario * d.Cantidad)
        };

        context.Pedidos.Add(pedido);
        await context.SaveChangesAsync(); // genera pedido.Id

        foreach (var d in detalles) d.PedidoId = pedido.Id;
        context.DetallePedidos.AddRange(detalles);
        await context.SaveChangesAsync();

        await tx.CommitAsync();
        return pedido;
    }
}