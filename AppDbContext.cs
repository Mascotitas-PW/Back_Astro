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
    public DbSet<PayPalCheckout> PayPalCheckouts => Set<PayPalCheckout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PayPalCheckout>(entity =>
        {
            entity.ToTable("PayPalCheckouts");
            entity.HasKey(checkout => checkout.OrderId);
            entity.Property(checkout => checkout.OrderId).HasColumnName("orderid");
            entity.Property(checkout => checkout.ReferenceId).HasColumnName("referenceid");
            entity.Property(checkout => checkout.UsuarioId).HasColumnName("usuarioid");
            entity.Property(checkout => checkout.ItemsJson).HasColumnName("itemsjson");
            entity.Property(checkout => checkout.MerchandiseAmount).HasColumnName("merchandiseamount").HasPrecision(12, 2);
            entity.Property(checkout => checkout.ShippingAmount).HasColumnName("shippingamount").HasPrecision(12, 2);
            entity.Property(checkout => checkout.TotalAmount).HasColumnName("totalamount").HasPrecision(12, 2);
            entity.Property(checkout => checkout.Currency).HasColumnName("currency");
            entity.Property(checkout => checkout.Status).HasColumnName("status");
            entity.Property(checkout => checkout.CaptureId).HasColumnName("captureid");
            entity.Property(checkout => checkout.PedidoId).HasColumnName("pedidoid");
            entity.Property(checkout => checkout.CreatedAt).HasColumnName("createdat");
            entity.HasIndex(checkout => checkout.UsuarioId);
        });

        
        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("Pedido");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Fecha).HasColumnName("fecha");
            e.Property(p => p.Total).HasColumnName("total");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.UsuarioId).HasColumnName("usuarioid");
        });

        modelBuilder.Entity<DetallePedido>(e =>
        {
            e.ToTable("DetallePedido");
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