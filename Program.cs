using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using back.GraphQL;
using HotChocolate;

var builder = WebApplication.CreateBuilder(args);
var jwtSettings = JwtTokenSettings.Load(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(new JwtTokenService(jwtSettings));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtSettings.SigningKey),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<MercadoPagoPaymentService>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<PayPalPaymentService>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                  (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
                  .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});


builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddFiltering()
    .AddSorting()
    .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = true);


var app = builder.Build();

//Middleware
app.UseCors("AllowReact");
app.UseAuthentication();
app.UseAuthorization();
app.MapGraphQL("/graphql");
app.MapPayPalCheckout();
app.MapPost("/process_order", async (
    ProcessOrderRequest request,
    MercadoPagoPaymentService payments,
    ILogger<Program> logger) =>
{
    try
    {
        return Results.Ok(await payments.ProcessAsync(request));
    }
    catch (PaymentRequestException exception)
    {
        return Results.BadRequest(new { status = "rejected", message = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "Mercado Pago is not configured.");
        return Results.Problem(
            title: "El servicio de pago no está configurado.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["status"] = "rejected" });
    }
});

app.Run();

public partial class Program;