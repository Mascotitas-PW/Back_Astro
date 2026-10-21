using back.Services.Auth;
using Microsoft.EntityFrameworkCore;
using HotChocolate;
using HotChocolate.Authorization;
using System.Security.Claims;
using back.Models;

public class Query
{
    // Obtener categorías
    public IQueryable<Categoria> GetCategorias(
        [Service] AppDbContext context)
    {
        return context.Categorias;
    }

    // Obtener productos con su categoría
    public IQueryable<ProductoDto> GetProductos(
        [Service] AppDbContext context)
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
                Categoria = p.Categoria != null
                    ? p.Categoria.Nombre
                    : "Sin Categoría"
            });
    }

    // Obtener todos los usuarios (solo administrador)
    [Authorize(Roles = new[] { "Admin" })]
    public IQueryable<User> GetUsers(
        [Service] AppDbContext context)
    {
        return context.Usuarios;
    }

    // Obtener todos los pedidos (solo administrador)
    [Authorize(Roles = new[] { "Admin" })]
    public Task<List<Pedido>> GetPedidos(
        [Service] AppDbContext context)
    {
        return context.Pedidos
            .AsNoTracking()
            .OrderByDescending(p => p.Fecha)
            .ToListAsync();
    }

    // Obtener pedidos de un usuario (administrador o propietario)
    [Authorize]
    public async Task<List<Pedido>> GetPedidosPorUsuario(
        int usuarioId,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        AuthHelper.ExigirAdminOPropietario(user, usuarioId);

        return await context.Pedidos
            .AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync();
    }

    // Obtener un pedido por ID (administrador o propietario)
    [Authorize]
    public async Task<Pedido?> GetPedidoPorId(
        int id,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var pedido = await context.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (pedido != null)
        {
            AuthHelper.ExigirAdminOPropietario(
                user, pedido.UsuarioId);
        }

        return pedido;
    }

    // Obtener detalles de un pedido (administrador o propietario)
    [Authorize]
    public async Task<List<DetallePedido>> GetDetallePedido(
        int pedidoId,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var pedido = await context.Pedidos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        if (pedido == null)
        {
            return new List<DetallePedido>();
        }

        AuthHelper.ExigirAdminOPropietario(
            user, pedido.UsuarioId);

        return await context.DetallePedidos
            .AsNoTracking()
            .Where(d => d.PedidoId == pedidoId)
            .ToListAsync();
    }
}

public class ProductoDto
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public decimal Precio { get; set; }

    public string Imagen { get; set; } = string.Empty;

    public int Stock { get; set; }

    public string Categoria { get; set; } = string.Empty;
}
