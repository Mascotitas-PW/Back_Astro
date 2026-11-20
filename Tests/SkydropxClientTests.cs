using System.Net;
using System.Text;
using back.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

public class SkydropxClientTests
{
    [Fact]
    public async Task CotizarAsync_WhenCredentialsMissing_ThrowsConfigurationException()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.OK, "{}"), new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<SkydropxConfigurationException>(() =>
            client.CotizarAsync(new DireccionCotizacion("44820", "Jalisco", "Guadalajara", "Figueroa"), 2m, default));
    }

    [Fact]
    public async Task CotizarAsync_WhenProviderRejectsAuthentication_ThrowsProviderException()
    {
        var configuration = CreateConfiguration();
        var handler = new StubHttpHandler(HttpStatusCode.Unauthorized,
            "{\"error\":\"invalid credentials\"}");
        var client = CreateClient(handler, configuration);

        await Assert.ThrowsAsync<SkydropxProviderException>(() =>
            client.CotizarAsync(new DireccionCotizacion("44820", "Jalisco", "Guadalajara", "Figueroa"), 2m, default));
    }

    [Fact]
    public async Task CotizarAsync_WhenProviderReturnsServerError_ThrowsProviderException()
    {
        var configuration = CreateConfiguration();
        var handler = new StubHttpHandler(HttpStatusCode.InternalServerError,
            "provider unavailable");
        var client = CreateClient(handler, configuration);

        await Assert.ThrowsAsync<SkydropxProviderException>(() =>
            client.CotizarAsync(new DireccionCotizacion("44820", "Jalisco", "Guadalajara", "Figueroa"), 2m, default));
    }

    [Fact]
    public async Task CotizarAsync_WhenConfigurationIsIncomplete_ThrowsConfigurationException()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Skydropx:Origin:PostalCode"] = ""
        });
        var handler = new StubHttpHandler(HttpStatusCode.OK,
            "{\"access_token\":\"test-token\",\"expires_in\":7200}");
        var client = CreateClient(handler, configuration);

        await Assert.ThrowsAsync<SkydropxConfigurationException>(() =>
            client.CotizarAsync(new DireccionCotizacion("44820", "Jalisco", "Guadalajara", "Figueroa"), 2m, default));
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? overrides = null)
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
        {
            foreach (var item in overrides)
                values[item.Key] = item.Value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static SkydropxClient CreateClient(HttpMessageHandler handler, IConfiguration configuration)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        return new SkydropxClient(
            new HttpClient(handler),
            configuration,
            new MemoryCache(new MemoryCacheOptions()),
            loggerFactory.CreateLogger<SkydropxClient>());
    }

    private sealed class StubHttpHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
