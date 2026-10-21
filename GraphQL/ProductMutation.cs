using back.Models;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace back.GraphQL;

[GraphQLName("ProductoInput")]
public record ProductoInput(
    string Nombre,
    decimal Precio,
    string Imagen,
    int Stock,
    int CategoriaId
);

[ExtendObjectType(typeof(Mutation))]
public class ProductoMutations
{
    [Authorize(Roles = new[] { "Admin" })]
    public async Task<Producto> CrearProducto(
        ProductoInput input,
        [Service] AppDbContext context,
        [Service] ITopicEventSender sender)
    {
        await Validar(input, context);

        var producto = new Producto
        {
            Nombre = input.Nombre.Trim(),
            Precio = input.Precio,
            Imagen = input.Imagen,
            Stock = input.Stock,
            CategoriaId = input.CategoriaId
        };

        context.Productos.Add(producto);
        await context.SaveChangesAsync();
        await sender.SendAsync(Subscription.TopicStock, new StockActualizado(producto.Id, producto.Stock));

        return producto;
    }

    [Authorize(Roles = new[] { "Admin" })]
    public async Task<Producto> ActualizarProducto(
        int id,
        ProductoInput input,
        [Service] AppDbContext context,
        [Service] ITopicEventSender sender)
    {
        await Validar(input, context);

        var producto = await context.Productos.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new GraphQLException($"El producto {id} no existe.");

        producto.Nombre = input.Nombre.Trim();
        producto.Precio = input.Precio;
        producto.Imagen = input.Imagen;
        producto.Stock = input.Stock;
        producto.CategoriaId = input.CategoriaId;

        await context.SaveChangesAsync();
        await sender.SendAsync(Subscription.TopicStock, new StockActualizado(producto.Id, producto.Stock));

        return producto;
    }

    [Authorize(Roles = new[] { "Admin" })]
    public async Task<bool> EliminarProducto(
        int id,
        [Service] AppDbContext context)
    {
        var producto = await context.Productos.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new GraphQLException($"El producto {id} no existe.");

        context.Productos.Remove(producto);
        await context.SaveChangesAsync();
        return true;
    }

    private static async Task Validar(ProductoInput input, AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(input.Nombre))
            throw new GraphQLException("El nombre es obligatorio.");
        if (input.Precio <= 0)
            throw new GraphQLException("El precio debe ser mayor a 0.");
        if (input.Stock < 0)
            throw new GraphQLException("El stock no puede ser negativo.");
        if (!await context.Categorias.AnyAsync(c => c.Id == input.CategoriaId))
            throw new GraphQLException($"La categoría {input.CategoriaId} no existe.");
    }
}
