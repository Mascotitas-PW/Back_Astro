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
using back.Services;
using back.Services.Auth;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));


builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IImageService, ImageService>();

builder.Services.AddHttpClient<MercadoPagoPaymentService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
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
    .AddTypeExtension<ProductoMutation>()
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
            url = $"{http.Request.Scheme}://{http.Request.Host}/images/{nombre}",
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

app.Run();
