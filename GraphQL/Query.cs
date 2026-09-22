using Microsoft.EntityFrameworkCore;

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

    public IQueryable<Categoria> GetCategorias([Service] AppDbContext context)
    {
        return context.Categorias;
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