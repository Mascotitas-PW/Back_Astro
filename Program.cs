using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using HotChocolate;

using back;
using back.GraphQL;
using back.Payments;
using back.Payments.PayPal;
using back.Services;
using back.Services.Pedidos;
using back.Services.Auth;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));


builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["CLOUDINARY_URL"]))
    builder.Services.AddSingleton<IImageService, CloudinaryImageService>();
else
    builder.Services.AddSingleton<IImageService, ImageService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<SkydropxClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<MercadoPagoPaymentService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<PayPalPaymentService>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<MexicoPostalService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 6 * 1024 * 1024;
});



var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Falta Jwt:Key.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "mascotitas",

            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "mascotitas",

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();



builder.Services.AddResponseCompression();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddTypeExtension<ProductMutation>()
    .AddTypeExtension<PedidoMutation>()
    .AddTypeExtension<DomicilioMutation>()
    .AddTypeExtension<DomicilioQueries>()
    .AddSubscriptionType<Subscription>()
    .AddInMemorySubscriptions()
    .AddFiltering()
    .AddSorting()
    .ModifyRequestOptions(options =>
    {
        options.IncludeExceptionDetails =
            builder.Environment.IsDevelopment();
    });


var app = builder.Build();


app.UseResponseCompression();
app.UseCors("AllowReact");

app.UseAuthentication();
app.UseAuthorization();

app.UseWebSockets();


var imagenes = app.Services.GetRequiredService<IImageService>();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        imagenes.RutaCarpeta
    ),
    RequestPath = "/images"
});


app.MapGraphQL("/graphql");


app.MapPost("/api/imagenes", async (
    IFormFile file,
    IImageService servicio,
    HttpContext http) =>
{
    try
    {
        var nombre = await servicio.GuardarAsync(file);

        return Results.Ok(new
        {
            url = nombre.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nombre
                : $"{http.Request.Scheme}://{http.Request.Host}/images/{nombre}",
            nombre
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new
        {
            error = ex.Message
        });
    }
})
.RequireAuthorization(policy => policy.RequireRole("Admin"))
.DisableAntiforgery();



app.MapProcessOrder();
app.MapPayPal();
app.MapEnvios();


app.MapPost("/webhook/mercadopago", async (
    HttpContext context,
    ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        logger.LogInformation(
            "Notificación de Mercado Pago recibida: {Body}",
            body
        );

        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "Error procesando el Webhook de Mercado Pago"
        );

        return Results.Ok();
    }
});

// Columnas de ubicación del pedido (idempotente: no falla si ya existen)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"Latitud\" double precision NULL");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"Longitud\" double precision NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Producto\" ADD COLUMN IF NOT EXISTS \"PesoKg\" numeric NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"CodigoPostalEntrega\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"EstadoEntrega\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"CiudadEntrega\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"ColoniaEntrega\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"CotizacionEnvioId\" uuid NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"SkydropxRateId\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"Paqueteria\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"ServicioEnvio\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pedido\" ADD COLUMN IF NOT EXISTS \"CostoEnvio\" numeric NULL");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CotizacionEnvio" (
                "Id" uuid PRIMARY KEY,
                "UsuarioId" integer NOT NULL,
                "SkydropxId" text NOT NULL,
                "CodigoPostal" text NOT NULL,
                "Estado" text NOT NULL,
                "Ciudad" text NOT NULL,
                "Colonia" text NOT NULL,
                "TarifasJson" text NOT NULL,
                "CreadaUtc" timestamp with time zone NOT NULL,
                "VenceUtc" timestamp with time zone NOT NULL)
            """);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CotizacionEnvio\" ADD COLUMN IF NOT EXISTS \"CodigoPostal\" text NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CotizacionEnvio\" ADD COLUMN IF NOT EXISTS \"Estado\" text NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CotizacionEnvio\" ADD COLUMN IF NOT EXISTS \"Ciudad\" text NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CotizacionEnvio\" ADD COLUMN IF NOT EXISTS \"Colonia\" text NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_CotizacionEnvio_UsuarioId\" ON \"CotizacionEnvio\" (\"UsuarioId\")");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Domicilio" (
                "Id" serial PRIMARY KEY,
                "UsuarioId" integer NOT NULL,
                "Alias" text NOT NULL,
                "NombreCompleto" text NOT NULL,
                "Telefono" text NOT NULL,
                "Direccion" text NOT NULL,
                "CodigoPostal" text NULL,
                "Latitud" double precision NULL,
                "Longitud" double precision NULL,
                "Predeterminado" boolean NOT NULL DEFAULT false)
            """);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Domicilio\" ADD COLUMN IF NOT EXISTS \"Estado\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Domicilio\" ADD COLUMN IF NOT EXISTS \"Ciudad\" text NULL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Domicilio\" ADD COLUMN IF NOT EXISTS \"Colonia\" text NULL");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_Domicilio_UsuarioId\" ON \"Domicilio\" (\"UsuarioId\")");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AceptacionLegal" (
                "Id" serial PRIMARY KEY,
                "UsuarioId" integer NOT NULL,
                "Version" text NOT NULL,
                "Fecha" timestamp with time zone NOT NULL)
            """);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "No se pudo preparar la base de datos (columnas/tablas nuevas).");
    }
}

app.MapGet("/api/codigos-postales/{codigoPostal}", async (
    string codigoPostal,
    MexicoPostalService postalService,
    CancellationToken cancellationToken) =>
{
    var resultado = await postalService.BuscarAsync(codigoPostal, cancellationToken);
    return resultado is null
        ? Results.NotFound(new { message = "Código postal no encontrado." })
        : Results.Ok(resultado);
});

app.Run();
