using Microsoft.EntityFrameworkCore;
using back.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Usuarios => Set<User>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<DetallePedido> DetallePedidos => Set<DetallePedido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tablas y columnas en minúsculas, igual que en la BDD
        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("pedidos");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Fecha).HasColumnName("fecha");
            e.Property(p => p.Total).HasColumnName("total");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.UsuarioId).HasColumnName("usuarioid");
        });

        modelBuilder.Entity<DetallePedido>(e =>
        {
            e.ToTable("detallepedidos");
            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.PedidoId).HasColumnName("pedidoid");
            e.Property(d => d.ProductoId).HasColumnName("productoid");
            e.Property(d => d.Cantidad).HasColumnName("cantidad");
            e.Property(d => d.PrecioUnitario).HasColumnName("preciounitario");
        });

        modelBuilder.Entity<Categoria>().HasData(
            new Categoria { Id = 1, Nombre = "Alimentos" },
            new Categoria { Id = 2, Nombre = "Juguetes" },
            new Categoria { Id = 3, Nombre = "Accesorios" }
        );

        modelBuilder.Entity<Producto>().HasData(
            new Producto { Id = 1, Nombre = "Croquetas CroqDog 5kg", Precio = 350.00m, Imagen = "croquetas.png", Stock = 20, CategoriaId = 1 },
            new Producto { Id = 2, Nombre = "Sobre Gourmet Gato", Precio = 25.50m, Imagen = "sobre.png", Stock = 50, CategoriaId = 1 },
            new Producto { Id = 3, Nombre = "Pelota de Goma Chillona", Precio = 80.00m, Imagen = "pelota.png", Stock = 15, CategoriaId = 2 },
            new Producto { Id = 4, Nombre = "Ratón con Plumas", Precio = 45.00m, Imagen = "raton.png", Stock = 30, CategoriaId = 2 },
            new Producto { Id = 5, Nombre = "Collar Antipulgas", Precio = 120.00m, Imagen = "collar.png", Stock = 10, CategoriaId = 3 },
            new Producto { Id = 6, Nombre = "Cama Acolchonada Grande", Precio = 450.00m, Imagen = "cama.png", Stock = 5, CategoriaId = 3 }
        );
    }
}