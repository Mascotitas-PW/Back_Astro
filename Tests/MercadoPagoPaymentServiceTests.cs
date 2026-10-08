using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using back.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class MercadoPagoPaymentServiceTests
{
    [Fact]
    public async Task ProcessAsync_WhenPaymentIsApproved_UsesServerCalculatedAmountAndReturnsApproved()
    {
        await using var context = CreateContext();
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "id": 987654321,
              "status": "approved",
              "transaction_amount": 350.00,
              "currency_id": "MXN"
            }
            """)
        });

        var service = CreateService(context, handler);
        var request = new ProcessOrderRequest(
            Token: "tok_test_123",
            PaymentMethodId: "visa",
            Installments: 1,
            UsuarioId: 1,
            Items: [new ProcessOrderItem(1, 2)],
            IssuerId: null,
            Payer: null,
            Email: "cliente@test.com",
            TransactionAmount: 999m);

        var result = await service.ProcessAsync(request);

        Assert.Equal("approved", result.Status);
        Assert.Equal(987654321, result.Id);
        Assert.Equal("tok_test_123", handler.LastRequestBody? ["token"]?.GetString());
        Assert.Equal(350m, handler.LastRequestBody?["transaction_amount"]?.GetDecimal());
    }

    [Fact]
    public async Task ProcessAsync_WhenMercadoPagoRejectsPayment_ReturnsRejectedStatus()
    {
        await using var context = CreateContext();
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
            {
              "message": "card declined",
              "error": "invalid_payment_method",
              "cause": [{ "code": "invalid_expiry", "description": "expiry date is invalid" }]
            }
            """)
        });

        var service = CreateService(context, handler);
        var request = new ProcessOrderRequest(
            Token: "tok_invalid",
            PaymentMethodId: "visa",
            Installments: 1,
            UsuarioId: 1,
            Items: [new ProcessOrderItem(1, 1)],
            IssuerId: null,
            Payer: null,
            Email: "cliente@test.com",
            TransactionAmount: 100m);

        var result = await service.ProcessAsync(request);

        Assert.Equal("rejected", result.Status);
        Assert.Contains("invalid_payment_method", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_WhenUserOrProductIsInvalid_ThrowsValidationException()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        var request = new ProcessOrderRequest(
            Token: "tok_ok",
            PaymentMethodId: "visa",
            Installments: 1,
            UsuarioId: 999,
            Items: [new ProcessOrderItem(999, 1)],
            IssuerId: null,
            Payer: null,
            Email: "cliente@test.com",
            TransactionAmount: 100m);

        await Assert.ThrowsAsync<PaymentRequestException>(() => service.ProcessAsync(request));
    }

    [Fact]
    public async Task ProcessAsync_WhenMercadoPagoReturnsServerError_PropagatesErrorMessage()
    {
        await using var context = CreateContext();
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""
            {
              "message": "Mercado Pago unavailable",
              "error": "mp_error"
            }
            """)
        });

        var service = CreateService(context, handler);
        var request = new ProcessOrderRequest(
            Token: "tok_ok",
            PaymentMethodId: "visa",
            Installments: 1,
            UsuarioId: 1,
            Items: [new ProcessOrderItem(1, 1)],
            IssuerId: null,
            Payer: null,
            Email: "cliente@test.com",
            TransactionAmount: 100m);

        var result = await service.ProcessAsync(request);

        Assert.Equal("rejected", result.Status);
        Assert.Contains("Mercado Pago unavailable", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Usuarios.Add(new User
        {
            Id = 1,
            Nombre = "Cliente",
            email = "cliente@test.com",
            Password = "hash",
            Rol = "Cliente"
        });
        context.Productos.Add(new Producto
        {
            Id = 1,
            Nombre = "Croquetas",
            Precio = 350.00m,
            Stock = 10,
            CategoriaId = 1,
            Imagen = "croquetas.png"
        });
        context.SaveChanges();
        return context;
    }

    private static MercadoPagoPaymentService CreateService(AppDbContext context, StubHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MP_TOKEN"] = "mp_test_token",
                ["MERCADOPAGO_CURRENCY"] = "MXN"
            })
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        return new MercadoPagoPaymentService(new HttpClient(handler), configuration, context, loggerFactory.CreateLogger<MercadoPagoPaymentService>());
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public JsonDocument? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var content = await request.Content.ReadAsStringAsync(cancellationToken);
                LastRequestBody = JsonDocument.Parse(content);
            }

            return response;
        }
    }
}
