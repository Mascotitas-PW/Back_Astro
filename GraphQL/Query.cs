using Microsoft.EntityFrameworkCore;
using HotChocolate;
using back.Models;

public class Query
{
    public IQueryable<ProductoDto> GetProductos([Service] AppDbContext context)
    {
        return context.Productos
            .Include(p => p.Categoria)
            .Select(p => new ProductoDto
            {
                Id = p.Id,
                Nombre = p.Nombre,
                Precio = p.Precio,
                Imagen = p.Imagen,
                Stock = p.Stock,
                Categoria = p.Categoria != null ? p.Categoria.Nombre : "Sin Categoría"
            });
    }

    public IQueryable<User> GetUsers([Service] AppDbContext context)
    {
        return context.Usuarios;
    }

    public Task<Pedido?> GetPedidoPorId(int id, [Service] AppDbContext context)
    {
        return context.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public Task<List<Pedido>> GetPedidosPorUsuario(int usuarioId, [Service] AppDbContext context)
    {
        return context.Pedidos
            .AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync();
    }


    public Task<List<DetallePedido>> GetDetallePedido(int pedidoId, [Service] AppDbContext context)
    {
        return context.DetallePedidos
            .AsNoTracking()
            .Where(d => d.PedidoId == pedidoId)
            .ToListAsync();
    }
}
