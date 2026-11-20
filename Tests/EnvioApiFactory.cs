using back;
using back.GraphQL;
using back.Models;
using back.Payments;
using back.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

/// <summary>
/// Levanta el pipeline real de la API (JWT, CORS, handler de excepciones, endpoint) con base de datos
/// en memoria y dobles de Skydropx / servicio postal. Nunca usa credenciales ni tokens reales.
/// </summary>
public sealed class EnvioApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "clave-solo-para-pruebas-0123456789-0123456789-abcdef";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly object _seedLock = new();
    private bool _seeded;

    public FakeSkydropxClient Skydropx { get; } = new();
    public FakePostalService Postal { get; } = new();

    static EnvioApiFactory()
    {
        // Program.cs lee estos valores al construir el builder (antes de que corran los callbacks del
        // factory), por eso se pasan como variables de entorno y no con ConfigureAppConfiguration.
        Environment.SetEnvironmentVariable("Jwt__Key", TestJwtKey);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1;Command Timeout=1");
        Environment.SetEnvironmentVariable("Skydropx__Parcel__TareWeightKg", "0.5");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            var dbServices = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType &&
                 d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration") &&
                 d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext)))).ToList();
            foreach (var descriptor in dbServices)
                services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_dbName));

            foreach (var descriptor in services.Where(d =>
                         d.ServiceType == typeof(ISkydropxClient) || d.ServiceType == typeof(IMexicoPostalService)).ToList())
                services.Remove(descriptor);
            services.AddSingleton<ISkydropxClient>(Skydropx);
            services.AddSingleton<IMexicoPostalService>(Postal);
        });
    }

    public void EnsureSeeded()
    {
        lock (_seedLock)
        {
            if (_seeded)
                return;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Productos.AddRange(
                new Producto { Id = 100, Nombre = "Arnés", Precio = 100m, Stock = 5, CategoriaId = 1, Imagen = "a.png", PesoKg = 0.8m },
                new Producto { Id = 101, Nombre = "Producto sin peso", Precio = 50m, Stock = 5, CategoriaId = 1, Imagen = "b.png", PesoKg = null });
            db.SaveChanges();
            _seeded = true;
        }
    }

    public string CreateToken(int userId = 7)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateToken(
            new User { Id = userId, Nombre = "Cliente de prueba", email = "cliente@test.local", Rol = "Cliente" });
    }

    /// <summary>JWT con firma de otra clave: debe rechazarse con 401.</summary>
    public static string CreateTokenSignedWithWrongKey()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }),
            Issuer = "mascotitas",
            Audience = "mascotitas",
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("otra-clave-distinta-0123456789-0123456789-zzzzzz")),
                SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

public sealed class FakeSkydropxClient : ISkydropxClient
{
    public Func<Task<ResultadoCotizacionSkydropx>> Behavior { get; set; } = DefaultBehavior;
    public DireccionCotizacion? LastDestino { get; private set; }
    public decimal? LastPesoKg { get; private set; }
    public int Calls { get; private set; }

    public static Task<ResultadoCotizacionSkydropx> DefaultBehavior() => Task.FromResult(
        new ResultadoCotizacionSkydropx("quotation-test",
        [
            new TarifaEnvio("rate-1", "Estafeta", "Terrestre", 120.50m, "MXN", 3),
            new TarifaEnvio("rate-2", "DHL", "Express", 210m, "MXN", null)
        ]));

    public void Reset()
    {
        Behavior = DefaultBehavior;
        LastDestino = null;
        LastPesoKg = null;
        Calls = 0;
    }

    public Task<ResultadoCotizacionSkydropx> CotizarAsync(DireccionCotizacion destino, decimal pesoKg, CancellationToken cancellationToken)
    {
        Calls++;
        LastDestino = destino;
        LastPesoKg = pesoKg;
        return Behavior();
    }
}

public sealed class FakePostalService : IMexicoPostalService
{
    public Func<Task<CodigoPostalMexicoResponse?>> Behavior { get; set; } = DefaultBehavior;

    public static Task<CodigoPostalMexicoResponse?> DefaultBehavior() => Task.FromResult<CodigoPostalMexicoResponse?>(
        new CodigoPostalMexicoResponse("44820", "Jalisco", "JAL", "Guadalajara", ["Figueroa"]));

    public void Reset() => Behavior = DefaultBehavior;

    public Task<CodigoPostalMexicoResponse?> BuscarAsync(string codigoPostal, CancellationToken cancellationToken) => Behavior();
}
