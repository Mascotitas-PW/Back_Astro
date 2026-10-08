using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using back.Models;

namespace Back.Tests;

public sealed class PayPalPaymentServiceTests
{
    [Fact]
    public async Task CreateOrderUsesDatabasePriceAndConfiguredShipping()
    {
        await using var database = await TestDatabase.CreateAsync();
        var handler = new FakePayPalHandler(
            JsonResponse("{\"access_token\":\"mock-token\"}"),
            JsonResponse("{\"id\":\"ORDER-1\",\"status\":\"CREATED\"}"));
        var service = CreateService(database.Context, handler);

        var result = await service.CreateOrderAsync(7, new PayPalCreateOrderRequest(7,
            [new PayPalOrderItemRequest(1, 1)]));

        Assert.Equal("ORDER-1", result.OrderId);
        Assert.Contains("\"value\":\"449.00\"", handler.Requests[1].Body);
        var checkout = await database.Context.PayPalCheckouts.SingleAsync();
        Assert.Equal(350m, checkout.MerchandiseAmount);
        Assert.Equal(99m, checkout.ShippingAmount);
        Assert.Equal(449m, checkout.TotalAmount);
        Assert.Equal(7, checkout.UsuarioId);
    }

    [Fact]
    public async Task CaptureReturnsCompletedOnlyWhenPayPalConfirmsExpectedAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedCheckoutAsync(database.Context);
        var handler = new FakePayPalHandler(
            JsonResponse("{\"access_token\":\"mock-token\"}"),
            RemoteCreatedOrder("449.00"),
            CaptureResponse("449.00"));

        var result = await CreateService(database.Context, handler).CaptureOrderAsync("ORDER-1", 7);

        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("CAPTURE-1", result.CaptureId);
        var checkout = await database.Context.PayPalCheckouts.SingleAsync();
        Assert.Equal("COMPLETED", checkout.Status);
        Assert.Equal("CAPTURE-1", checkout.CaptureId);
    }

    [Fact]
    public async Task RepeatedCaptureIsIdempotentAndDoesNotCallPayPalAgain()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedCheckoutAsync(database.Context, status: "COMPLETED", captureId: "CAPTURE-1");
        var handler = new FakePayPalHandler();

        var result = await CreateService(database.Context, handler).CaptureOrderAsync("ORDER-1", 7);

        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("CAPTURE-1", result.CaptureId);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CaptureRejectsAnAmountThatDiffersFromStoredCheckout()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedCheckoutAsync(database.Context);
        var handler = new FakePayPalHandler(
            JsonResponse("{\"access_token\":\"mock-token\"}"),
            RemoteCompletedOrder("449.00", "448.00"));

        var exception = await Assert.ThrowsAsync<PayPalCheckoutException>(() =>
            CreateService(database.Context, handler).CaptureOrderAsync("ORDER-1", 7));

        Assert.Equal(502, exception.StatusCode);
        Assert.Equal("CREATED", (await database.Context.PayPalCheckouts.SingleAsync()).Status);
    }

    [Fact]
    public async Task CaptureRejectsAnotherUsersCheckoutBeforeCallingPayPal()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedCheckoutAsync(database.Context);
        var handler = new FakePayPalHandler();

        var exception = await Assert.ThrowsAsync<PayPalCheckoutException>(() =>
            CreateService(database.Context, handler).CaptureOrderAsync("ORDER-1", 8));

        Assert.Equal(403, exception.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void UnauthenticatedPrincipalCannotResolveCheckoutIdentity()
    {
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "7")]));
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "7")], "test"));

        Assert.False(PayPalCheckoutEndpoints.TryGetAuthenticatedUserId(unauthenticated, out _));
        Assert.True(PayPalCheckoutEndpoints.TryGetAuthenticatedUserId(authenticated, out var userId));
        Assert.Equal(7, userId);
    }

    [Fact]
    public async Task PayPalApiErrorsBecomeGatewayErrors()
    {
        await using var database = await TestDatabase.CreateAsync();
        var handler = new FakePayPalHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var exception = await Assert.ThrowsAsync<PayPalCheckoutException>(() =>
            CreateService(database.Context, handler).CreateOrderAsync(7,
                new PayPalCreateOrderRequest(7, [new PayPalOrderItemRequest(1, 1)])));

        Assert.Equal(502, exception.StatusCode);
    }

    private static PayPalPaymentService CreateService(AppDbContext context, FakePayPalHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PUBLIC_PP_CLIENT"] = "test-client",
            ["PP_KEY_SECRET"] = "test-secret",
            ["PAYPAL_ENVIRONMENT"] = "Sandbox"
        }).Build();
        return new PayPalPaymentService(new HttpClient(handler), configuration, context);
    }

    private static async Task SeedCheckoutAsync(
        AppDbContext context,
        string status = "CREATED",
        string? captureId = null)
    {
        context.PayPalCheckouts.Add(new PayPalCheckout
        {
            OrderId = "ORDER-1",
            ReferenceId = "REF-1",
            UsuarioId = 7,
            ItemsJson = "[]",
            MerchandiseAmount = 350m,
            ShippingAmount = 99m,
            TotalAmount = 449m,
            Currency = "MXN",
            Status = status,
            CaptureId = captureId,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private static HttpResponseMessage RemoteCreatedOrder(string amount) => JsonResponse(
        $"{{\"id\":\"ORDER-1\",\"status\":\"APPROVED\",\"purchase_units\":[{{\"custom_id\":\"REF-1\",\"amount\":{{\"currency_code\":\"MXN\",\"value\":\"{amount}\"}}}}]}}");

    private static HttpResponseMessage RemoteCompletedOrder(string orderAmount, string captureAmount) => JsonResponse(
        $"{{\"id\":\"ORDER-1\",\"status\":\"COMPLETED\",\"purchase_units\":[{{\"custom_id\":\"REF-1\",\"amount\":{{\"currency_code\":\"MXN\",\"value\":\"{orderAmount}\"}},\"payments\":{{\"captures\":[{{\"id\":\"CAPTURE-1\",\"status\":\"COMPLETED\",\"amount\":{{\"currency_code\":\"MXN\",\"value\":\"{captureAmount}\"}}}}]}}}}]}}");

    private static HttpResponseMessage CaptureResponse(string amount) => RemoteCompletedOrder(amount, amount);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, AppDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Usuarios.Add(new User { Id = 7, Nombre = "Test", email = "test@example.test", Password = "hash", Rol = "Cliente" });
            await context.SaveChangesAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakePayPalHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return responses.Dequeue();
        }
    }
}