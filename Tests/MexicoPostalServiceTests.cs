using System.Net;
using System.Text;
using back.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class MexicoPostalServiceTests
{
    private const string ZippopotamResponse =
        """{"places":[{"place name":"Figueroa","state":"Jalisco","state abbreviation":"JAL"}]}""";

    [Fact]
    public async Task BuscarAsync_WhenProviderReturns404_ReturnsNull()
    {
        var service = CreateService(_ => Json("{}", HttpStatusCode.NotFound));

        Assert.Null(await service.BuscarAsync("00000", default));
    }

    [Fact]
    public async Task BuscarAsync_WhenProviderFails_ThrowsInsteadOfReportingMissingPostalCode()
    {
        var service = CreateService(_ => Json("{}", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.BuscarAsync("44820", default));
    }

    [Fact]
    public async Task BuscarAsync_WhenCityLookupFails_FallsBackToState()
    {
        var service = CreateService(request => request.RequestUri!.Host.Contains("zippopotam")
            ? Json(ZippopotamResponse)
            : throw new HttpRequestException("nominatim no disponible"));

        var result = await service.BuscarAsync("44820", default);

        Assert.NotNull(result);
        Assert.Equal("Jalisco", result!.Ciudad);
        Assert.Contains("Figueroa", result.Colonias);
    }

    [Fact]
    public async Task BuscarAsync_UsesCityFromNominatimWhenAvailable()
    {
        var service = CreateService(request => request.RequestUri!.Host.Contains("zippopotam")
            ? Json(ZippopotamResponse)
            : Json("""[{"address":{"city":"Guadalajara"}}]"""));

        Assert.Equal("Guadalajara", (await service.BuscarAsync("44820", default))?.Ciudad);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MexicoPostalService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FuncHandler(handler)), NullLogger<MexicoPostalService>.Instance);

    private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
