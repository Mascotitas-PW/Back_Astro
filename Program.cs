using Microsoft.EntityFrameworkCore;
using back.GraphQL;
using back.Payments;
using HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<MercadoPagoPaymentService>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
        policy.AllowAnyOrigin()
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

app.UseCors("AllowReact");

app.MapGraphQL("/graphql");

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
            body);

        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "Error procesando el Webhook de Mercado Pago");

        return Results.Ok();
    }
});

app.Run();