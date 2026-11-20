using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using back;
using back.Payments;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class EnvioEndpointTests : IClassFixture<EnvioApiFactory>
{
    private const string Path = "/api/envios/cotizaciones";
    private const string LocalOrigin = "http://localhost:4321";
    private const string ProdOrigin = "https://mascotitas-pw.github.io";

    private readonly EnvioApiFactory _factory;

    public EnvioEndpointTests(EnvioApiFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
        _factory.Skydropx.Reset();
        _factory.Postal.Reset();
    }

    private static object ValidPayload(int productoId = 100, int cantidad = 2) => new
    {
        items = new[] { new { productoId, cantidad } },
        codigoPostal = "44820",
        estado = "Jalisco",
        ciudad = "Guadalajara",
        colonia = "Figueroa"
    };

    private Task<HttpResponseMessage> PostAsync(
        HttpContent content, string? token, string origin = LocalOrigin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = content };
        request.Headers.Add("Origin", origin);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _factory.CreateClient().SendAsync(request);
    }

    private Task<HttpResponseMessage> PostJsonAsync(object payload, string? token, string origin = LocalOrigin) =>
        PostAsync(JsonContent.Create(payload), token, origin);

    private static string? AllowOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.FirstOrDefault() : null;

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("message").GetString() ?? "";
    }

    [Theory]
    [InlineData(LocalOrigin)]
    [InlineData(ProdOrigin)]
    public async Task Cotizar_ValidAuthenticated_ReturnsQuoteWithIdAndRates(string origin)
    {
        var response = await PostJsonAsync(ValidPayload(), _factory.CreateToken(userId: 7), origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, AllowOrigin(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.True(root.TryGetProperty("venceUtc", out _));

        // Contrato que consume el frontend (camelCase).
        var tarifas = root.GetProperty("tarifas");
        Assert.Equal(2, tarifas.GetArrayLength());
        var primera = tarifas[0];
        Assert.Equal("rate-1", primera.GetProperty("id").GetString());
        Assert.Equal("Estafeta", primera.GetProperty("paqueteria").GetString());
        Assert.Equal("Terrestre", primera.GetProperty("servicio").GetString());
        Assert.Equal(120.50m, primera.GetProperty("importe").GetDecimal());
        Assert.Equal("MXN", primera.GetProperty("moneda").GetString());
        Assert.Equal(3, primera.GetProperty("diasEntrega").GetInt32());
        Assert.Equal(JsonValueKind.Null, tarifas[1].GetProperty("diasEntrega").ValueKind);

        // Peso = tara (0.5, configurada por el factory) + 0.8 kg * 2 piezas.
        Assert.Equal(2.1m, _factory.Skydropx.LastPesoKg);
        Assert.Equal("44820", _factory.Skydropx.LastDestino?.CodigoPostal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guardada = db.CotizacionesEnvio.Single(c => c.Id == id);
        Assert.Equal(7, guardada.UsuarioId);
        Assert.Equal("quotation-test", guardada.SkydropxId);
    }

    [Fact]
    public async Task Cotizar_WithoutToken_Returns401WithCors()
    {
        var response = await PostJsonAsync(ValidPayload(), token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
        Assert.Equal(0, _factory.Skydropx.Calls);
    }

    [Theory]
    [InlineData("esto-no-es-un-jwt")]
    public async Task Cotizar_WithMalformedToken_Returns401(string token)
    {
        var response = await PostJsonAsync(ValidPayload(), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
    }

    [Fact]
    public async Task Cotizar_WithTokenSignedByAnotherKey_Returns401()
    {
        var response = await PostJsonAsync(ValidPayload(), EnvioApiFactory.CreateTokenSignedWithWrongKey());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
    }

    public static TheoryData<string> InvalidPayloads => new()
    {
        """{"items":[{"productoId":100,"cantidad":1}],"codigoPostal":"448","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":100,"cantidad":1}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":""}""",
        """{"items":[],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":100,"cantidad":0}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":100,"cantidad":-2}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":0,"cantidad":1}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":100,"cantidad":2147483647},{"productoId":100,"cantidad":2147483647}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":999999,"cantidad":1}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}""",
        """{"items":[{"productoId":101,"cantidad":1}],"codigoPostal":"44820","estado":"Jalisco","ciudad":"Guadalajara","colonia":"Figueroa"}"""
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Cotizar_InvalidPayload_Returns400WithUsefulMessageAndCors(string json)
    {
        var response = await PostAsync(
            new StringContent(json, Encoding.UTF8, "application/json"), _factory.CreateToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
        Assert.False(string.IsNullOrWhiteSpace(await MessageAsync(response)));
        Assert.Equal(0, _factory.Skydropx.Calls);
    }

    [Theory]
    [InlineData("{no es json")]
    [InlineData("")]
    public async Task Cotizar_MalformedOrEmptyBody_Returns400NotServerError(string body)
    {
        var response = await PostAsync(
            new StringContent(body, Encoding.UTF8, "application/json"), _factory.CreateToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
    }

    [Fact]
    public async Task Cotizar_UnknownPostalCode_Returns404()
    {
        _factory.Postal.Behavior = () => Task.FromResult<CodigoPostalMexicoResponse?>(null);

        var response = await PostJsonAsync(ValidPayload(), _factory.CreateToken());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
        Assert.Equal(0, _factory.Skydropx.Calls);
    }

    [Theory]
    [InlineData(typeof(SkydropxProviderException), 502)]
    [InlineData(typeof(SkydropxConfigurationException), 503)]
    [InlineData(typeof(HttpRequestException), 502)]
    [InlineData(typeof(TimeoutException), 504)]
    [InlineData(typeof(JsonException), 502)]
    public async Task Cotizar_ProviderFailure_ReturnsControlledStatusWithoutLeakingDetails(Type exceptionType, int expectedStatus)
    {
        const string internalDetail = "Falta Skydropx:ClientSecret en el servidor interno";
        _factory.Skydropx.Behavior = () =>
        {
            if (exceptionType == typeof(SkydropxProviderException))
                throw new SkydropxProviderException("Skydropx no pudo crear la cotización.", 502);
            throw (Exception)Activator.CreateInstance(exceptionType, internalDetail)!;
        };

        var response = await PostJsonAsync(ValidPayload(), _factory.CreateToken());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal(LocalOrigin, AllowOrigin(response));
        Assert.DoesNotContain("ClientSecret", body);
        Assert.DoesNotContain(" at ", body);
    }

    [Theory]
    [InlineData(LocalOrigin)]
    [InlineData(ProdOrigin)]
    public async Task Cotizar_UnexpectedException_Returns500WithCorsHeadersAndNoDetails(string origin)
    {
        _factory.Skydropx.Behavior = () => throw new InvalidCastException("detalle-interno-secreto");

        var response = await PostJsonAsync(ValidPayload(), _factory.CreateToken(), origin);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(origin, AllowOrigin(response));
        Assert.DoesNotContain("detalle-interno-secreto", body);
        Assert.DoesNotContain("InvalidCastException", body);
        Assert.False(string.IsNullOrWhiteSpace(await MessageAsync(response)));
    }

    [Fact]
    public async Task Cotizar_UnexpectedExceptionInPostalService_Returns500WithCors()
    {
        _factory.Postal.Behavior = () => throw new NullReferenceException("detalle-interno-secreto");

        var response = await PostJsonAsync(ValidPayload(), _factory.CreateToken(), ProdOrigin);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProdOrigin, AllowOrigin(response));
        Assert.DoesNotContain("detalle-interno-secreto", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cotizar_DisallowedOrigin_DoesNotGetCorsHeaders()
    {
        _factory.Skydropx.Behavior = () => throw new InvalidCastException("x");

        var response = await PostJsonAsync(ValidPayload(), _factory.CreateToken(), "https://evil.example");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(AllowOrigin(response)); // ni '*' ni el origen ajeno
    }

    [Theory]
    [InlineData(LocalOrigin)]
    [InlineData(ProdOrigin)]
    public async Task Preflight_AllowsPostAuthorizationAndContentType(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, Path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, AllowOrigin(response));
        Assert.Contains("POST", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")));
        var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("authorization", allowedHeaders);
        Assert.Contains("content-type", allowedHeaders);
    }
}
