using Microsoft.EntityFrameworkCore;
using back.GraphQL;
using HotChocolate; 
var builder = WebApplication.CreateBuilder(args);

// 1. Configurar DbContext con PostgreSQL (Supabase)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Configurar CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// 3. Configurar servidor GraphQL
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddFiltering()
    .AddSorting()
    .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = true);

var app = builder.Build();

// 4. Middlewares de la aplicación
app.UseCors("AllowReact");
app.MapGraphQL("/graphql");

app.Run();

// ==========================================
// ENTIDADES Y DB CONTEXT
// ==========================================

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class Producto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string Imagen { get; set; } = string.Empty;
    public int Stock { get; set; }
    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Usuarios => Set<User>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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