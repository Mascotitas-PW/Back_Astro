using Microsoft.EntityFrameworkCore;
using HotChocolate;
using HotChocolate.Authorization;
using System.Security.Claims;
using back.Models;
using back.Services.Auth;

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
                CategoriaId = p.CategoriaId,
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

    // Estadísticas para el panel de administración (solo administrador)
    [Authorize(Roles = new[] { "Admin" })]
    public async Task<EstadisticasDto> GetEstadisticas(
        [Service] AppDbContext context)
    {
        var pedidos = await context.Pedidos.AsNoTracking().ToListAsync();
        var detalles = await context.DetallePedidos.AsNoTracking().ToListAsync();
        var productos = await context.Productos
            .AsNoTracking()
            .Include(p => p.Categoria)
            .ToDictionaryAsync(p => p.Id);

        var totalVentas = pedidos.Sum(p => (decimal)p.Total);

        // Últimos 30 días, incluyendo los días sin ventas
        var hoy = DateTime.UtcNow.Date;
        var porDia = pedidos
            .GroupBy(p => p.Fecha.Date)
            .ToDictionary(g => g.Key, g => g);

        var ventasPorDia = Enumerable.Range(0, 30)
            .Select(i => hoy.AddDays(i - 29))
            .Select(dia => new VentaDiaDto
            {
                Fecha = dia.ToString("yyyy-MM-dd"),
                Total = porDia.TryGetValue(dia, out var g)
                    ? g.Sum(p => (decimal)p.Total) : 0,
                Pedidos = porDia.TryGetValue(dia, out var g2) ? g2.Count() : 0
            })
            .ToList();

        var ventasProducto = detalles
            .GroupBy(d => d.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                Unidades = g.Sum(d => d.Cantidad),
                Ingresos = g.Sum(d => (decimal)d.PrecioUnitario * d.Cantidad)
            })
            .ToList();

        var top = ventasProducto
            .OrderByDescending(v => v.Unidades)
            .Take(5)
            .Select(v => new TopProductoDto
            {
                ProductoId = v.ProductoId,
                Nombre = productos.TryGetValue(v.ProductoId, out var p)
                    ? p.Nombre : "Producto eliminado",
                Unidades = v.Unidades,
                Ingresos = v.Ingresos
            })
            .ToList();

        var porCategoria = ventasProducto
            .GroupBy(v => productos.TryGetValue(v.ProductoId, out var p)
                && p.Categoria != null ? p.Categoria.Nombre : "Sin categoría")
            .Select(g => new VentaCategoriaDto
            {
                Categoria = g.Key,
                Ingresos = g.Sum(v => v.Ingresos)
            })
            .OrderByDescending(c => c.Ingresos)
            .ToList();

        return new EstadisticasDto
        {
            TotalVentas = totalVentas,
            TotalPedidos = pedidos.Count,
            TicketPromedio = pedidos.Count == 0 ? 0 : totalVentas / pedidos.Count,
            VentasPorDia = ventasPorDia,
            TopProductos = top,
            VentasPorCategoria = porCategoria
        };
    }
     public class ProductoDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string Imagen { get; set; } = string.Empty;
        public int Stock { get; set; }
        public int CategoriaId { get; set; }
        public string Categoria { get; set; } = string.Empty;
    }
}
