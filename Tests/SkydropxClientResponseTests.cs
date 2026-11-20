using System.Net;
using System.Text;
using System.Text.Json;
using back.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class SkydropxClientResponseTests
{
    private static readonly DireccionCotizacion Destino = new("44820", "Jalisco", "Guadalajara", "Figueroa");

    [Fact]
    public async Task CotizarAsync_ValidResponse_MapsRates()
    {
        var client = CreateClient(Provider(Quotation(RateJson(days: "3"))));

        var result = await client.CotizarAsync(Destino, 2m, default);

        Assert.Equal("q1", result.Id);
        var rate = Assert.Single(result.Tarifas);
        Assert.Equal(("r1", "Estafeta", "Terrestre", 150.50m, "MXN", 3),
            (rate.Id, rate.Paqueteria, rate.Servicio, rate.Importe, rate.Moneda, rate.DiasEntrega));
    }

    [Fact]
    public async Task CotizarAsync_WhenDaysIsNull_DoesNotThrowAndLeavesDaysEmpty()
    {
        // JsonElement.TryGetInt32 lanza InvalidOperationException con null/texto.
        var client = CreateClient(Provider(Quotation(RateJson(days: "null"))));

        var result = await client.CotizarAsync(Destino, 2m, default);

        Assert.Null(Assert.Single(result.Tarifas).DiasEntrega);
    }

    [Fact]
    public async Task CotizarAsync_WhenTotalIsNumberAndDaysIsText_ParsesBoth()
    {
        var rate = """{"id":"r2","success":true,"provider_display_name":"FedEx","provider_service_name":"Std","total":99.9,"days":"2"}""";
        var client = CreateClient(Provider(Quotation(rate)));

        var tarifa = Assert.Single((await client.CotizarAsync(Destino, 2m, default)).Tarifas);

        Assert.Equal(99.9m, tarifa.Importe);
        Assert.Equal(2, tarifa.DiasEntrega);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"texto\"")]
    public async Task CotizarAsync_WhenProviderReturnsNonObject_ThrowsProviderException(string body)
    {
        var client = CreateClient(Provider(body));

        await Assert.ThrowsAsync<SkydropxProviderException>(() => client.CotizarAsync(Destino, 2m, default));
    }

    [Fact]
    public async Task CotizarAsync_WhenProviderReturnsHtml_ThrowsJsonException()
    {
        var client = CreateClient(Provider("<html>oops</html>"));

        await Assert.ThrowsAnyAsync<JsonException>(() => client.CotizarAsync(Destino, 2m, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 503)]
    [InlineData(HttpStatusCode.TooManyRequests, 503)]
    [InlineData(HttpStatusCode.InternalServerError, 502)]
    [InlineData(HttpStatusCode.UnprocessableEntity, 502)]
    public async Task CotizarAsync_MapsProviderStatusAndHidesRawBody(HttpStatusCode providerStatus, int expected)
    {
        var client = CreateClient(_ => Json("detalle-crudo-del-proveedor", providerStatus));

        var ex = await Assert.ThrowsAsync<SkydropxProviderException>(() => client.CotizarAsync(Destino, 2m, default));

        Assert.Equal(expected, ex.StatusCode);
        Assert.DoesNotContain("detalle-crudo", ex.Message);
    }

    [Fact]
    public async Task CotizarAsync_On401_EvictsCachedToken()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("Skydropx.AccessToken", "token-viejo", TimeSpan.FromHours(1));
        var client = CreateClient(_ => Json("{}", HttpStatusCode.Unauthorized), cache: cache);

        await Assert.ThrowsAsync<SkydropxProviderException>(() => client.CotizarAsync(Destino, 2m, default));

        Assert.False(cache.TryGetValue("Skydropx.AccessToken", out _));
    }

    [Theory]
    [InlineData("Skydropx:Parcel:LengthCm", "30,5")]
    [InlineData("Skydropx:Parcel:WidthCm", "abc")]
    public async Task CotizarAsync_WhenParcelSettingIsNotANumber_ThrowsConfigurationException(string key, string value)
    {
        // "30,5" no debe leerse como 305 (coma como separador de miles).
        var client = CreateClient(Provider(Quotation(RateJson("3"))), new() { [key] = value });

        await Assert.ThrowsAsync<SkydropxConfigurationException>(() => client.CotizarAsync(Destino, 2m, default));
    }

    private static string RateJson(string days) =>
        $$"""{"id":"r1","success":true,"provider_display_name":"Estafeta","provider_service_name":"Terrestre","total":"150.50","currency_code":"MXN","days":{{days}}}""";

    private static string Quotation(string rate) => $$"""{"is_completed":true,"rates":[{{rate}}]}""";

    private static Func<HttpRequestMessage, HttpResponseMessage> Provider(string quotationBody) => request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/oauth/token")) return Json("""{"access_token":"t","expires_in":7200}""");
        if (request.Method == HttpMethod.Post) return Json("""{"id":"q1"}""");
        return Json(quotationBody);
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SkydropxClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        Dictionary<string, string?>? overrides = null,
        IMemoryCache? cache = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Skydropx:BaseUrl"] = "https://sandbox.example.test",
            ["Skydropx:ClientId"] = "test-client",
            ["Skydropx:ClientSecret"] = "test-secret",
            ["Skydropx:Origin:PostalCode"] = "44100",
            ["Skydropx:Origin:State"] = "Jalisco",
            ["Skydropx:Origin:City"] = "Guadalajara",
            ["Skydropx:Origin:Neighborhood"] = "Centro",
            ["Skydropx:Parcel:LengthCm"] = "30",
            ["Skydropx:Parcel:WidthCm"] = "20",
            ["Skydropx:Parcel:HeightCm"] = "10"
        };
        if (overrides is not null)
            foreach (var item in overrides) values[item.Key] = item.Value;

        return new SkydropxClient(
            new HttpClient(new FuncHandler(handler)),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SkydropxClient>.Instance);
    }

    private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
