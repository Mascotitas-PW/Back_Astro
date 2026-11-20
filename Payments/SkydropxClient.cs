using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace back.Payments;

public sealed record DireccionCotizacion(string CodigoPostal, string Estado, string Ciudad, string Colonia);

public sealed record ResultadoCotizacionSkydropx(string Id, IReadOnlyList<back.Models.TarifaEnvio> Tarifas);

public interface ISkydropxClient
{
    Task<ResultadoCotizacionSkydropx> CotizarAsync(
        DireccionCotizacion destino,
        decimal pesoKg,
        CancellationToken cancellationToken);
}

public sealed class SkydropxClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<SkydropxClient> logger) : ISkydropxClient
{
    private const string TokenCacheKey = "Skydropx.AccessToken";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim ApiThrottle = new(1, 1);
    private static DateTime NextApiRequestUtc;

    public async Task<ResultadoCotizacionSkydropx> CotizarAsync(
        DireccionCotizacion destino,
        decimal pesoKg,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Skydropx:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiBase))
            throw new SkydropxConfigurationException("Configura Skydropx:BaseUrl con el host Sandbox de tu cuenta.");

        httpClient.BaseAddress = apiBase;
        var token = await ObtenerTokenAsync(cancellationToken);

        var origin = configuration.GetSection("Skydropx:Origin");
        var parcel = configuration.GetSection("Skydropx:Parcel");
        var length = SkydropxSettings.ReadDecimal(configuration, "Skydropx:Parcel:LengthCm");
        var width = SkydropxSettings.ReadDecimal(configuration, "Skydropx:Parcel:WidthCm");
        var height = SkydropxSettings.ReadDecimal(configuration, "Skydropx:Parcel:HeightCm");

        if (length <= 0 || width <= 0 || height <= 0)
            throw new SkydropxConfigurationException("Configura largo, ancho y alto positivos en Skydropx:Parcel.");
        if (string.IsNullOrWhiteSpace(origin["PostalCode"]) ||
            string.IsNullOrWhiteSpace(origin["State"]) ||
            string.IsNullOrWhiteSpace(origin["City"]) ||
            string.IsNullOrWhiteSpace(origin["Neighborhood"]))
            throw new SkydropxConfigurationException("Configura el código postal, estado, ciudad y colonia de origen en Skydropx:Origin.");

        var payload = new
        {
            quotation = new
            {
                address_from = new
                {
                    country_code = "MX",
                    postal_code = origin["PostalCode"],
                    area_level1 = origin["State"],
                    area_level2 = origin["City"],
                    area_level3 = origin["Neighborhood"]
                },
                address_to = new
                {
                    country_code = "MX",
                    postal_code = destino.CodigoPostal,
                    area_level1 = destino.Estado,
                    area_level2 = destino.Ciudad,
                    area_level3 = destino.Colonia
                },
                parcels = new[]
                {
                    new { length, width, height, weight = pesoKg }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/quotations")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendThrottledAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CrearErrorAsync(response, "Skydropx no pudo crear la cotización.", cancellationToken);

        using var created = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        ExigirObjeto(created.RootElement, "la creación de la cotización");
        var quotationId = LeerTexto(created.RootElement, "id");
        if (string.IsNullOrWhiteSpace(quotationId))
            throw new SkydropxProviderException("Skydropx no devolvió el identificador de cotización.");

        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            using var poll = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/quotations/{Uri.EscapeDataString(quotationId)}");
            poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var quoteResponse = await SendThrottledAsync(poll, cancellationToken);
            if (!quoteResponse.IsSuccessStatusCode)
                throw await CrearErrorAsync(quoteResponse, "No se pudo consultar la cotización de Skydropx.", cancellationToken);

            using var quote = await JsonDocument.ParseAsync(
                await quoteResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            ExigirObjeto(quote.RootElement, "la consulta de la cotización");
            if (quote.RootElement.TryGetProperty("is_completed", out var completed) &&
                completed.ValueKind == JsonValueKind.True)
            {
                return new ResultadoCotizacionSkydropx(quotationId, LeerTarifas(quote.RootElement));
            }
        }

        throw new TimeoutException("Skydropx sigue procesando la cotización. Intenta de nuevo en unos segundos.");
    }

    private async Task<string> ObtenerTokenAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = TokenCacheKey;
        if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        var clientId = configuration["Skydropx:ClientId"];
        var clientSecret = configuration["Skydropx:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new SkydropxConfigurationException("Configura Skydropx:ClientId y Skydropx:ClientSecret como secretos del backend.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/oauth/token")
        {
            Content = form
        };
        using var response = await SendThrottledAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CrearErrorAsync(response, "No se pudo autenticar con Skydropx.", cancellationToken);

        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        ExigirObjeto(body.RootElement, "la autenticación");
        var accessToken = LeerTexto(body.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new SkydropxProviderException("Skydropx no devolvió un bearer token.");

        var expiresIn = body.RootElement.TryGetProperty("expires_in", out var expires) &&
                        expires.ValueKind == JsonValueKind.Number &&
                        expires.TryGetInt32(out var seconds) ? seconds : 7200;
        cache.Set(cacheKey, accessToken, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return accessToken;
    }

    private static List<back.Models.TarifaEnvio> LeerTarifas(JsonElement root)
    {
        var rates = new List<back.Models.TarifaEnvio>();
        if (root.TryGetProperty("rates", out var directRates) && directRates.ValueKind == JsonValueKind.Array)
            AgregarTarifas(directRates, rates);

        if (rates.Count == 0 && root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in included.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    LeerTexto(item, "type") == "rate" &&
                    item.TryGetProperty("attributes", out var attributes) &&
                    attributes.ValueKind == JsonValueKind.Object)
                    AgregarTarifa(item, attributes, rates);
            }
        }

        return rates;
    }

    private static void AgregarTarifas(JsonElement ratesElement, List<back.Models.TarifaEnvio> rates)
    {
        foreach (var rate in ratesElement.EnumerateArray())
        {
            if (rate.ValueKind == JsonValueKind.Object)
                AgregarTarifa(rate, rate, rates);
        }
    }

    private static void AgregarTarifa(JsonElement identity, JsonElement attributes, List<back.Models.TarifaEnvio> rates)
    {
        var id = LeerTexto(identity, "id");
        var success = attributes.TryGetProperty("success", out var successValue) && successValue.ValueKind == JsonValueKind.True;
        var carrier = LeerTexto(attributes, "provider_display_name") ?? LeerTexto(attributes, "provider_name");
        var service = LeerTexto(attributes, "provider_service_name");
        var currency = LeerTexto(attributes, "currency_code") ?? "MXN";
        var amountText = LeerTexto(attributes, "total") ?? LeerTexto(attributes, "amount");
        if (!success || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(carrier) ||
            string.IsNullOrWhiteSpace(service) || !decimal.TryParse(amountText, System.Globalization.CultureInfo.InvariantCulture, out var amount))
            return;

        int? days = null;
        if (attributes.TryGetProperty("days", out var daysValue))
        {
            // TryGetInt32 lanza InvalidOperationException si el valor es null o texto; Skydropx
            // devuelve "days": null cuando la paquetería no estima tiempo de entrega.
            if (daysValue.ValueKind == JsonValueKind.Number && daysValue.TryGetInt32(out var parsedDays))
                days = parsedDays;
            else if (daysValue.ValueKind == JsonValueKind.String &&
                     int.TryParse(daysValue.GetString(), System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture, out var textDays))
                days = textDays;
        }
        rates.Add(new back.Models.TarifaEnvio(id, carrier, service, amount, currency, days));
    }

    private static string? LeerTexto(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static void ExigirObjeto(JsonElement element, string contexto)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new SkydropxProviderException($"Skydropx devolvió una respuesta inesperada en {contexto}.");
    }

    private async Task<HttpResponseMessage> SendThrottledAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await ApiThrottle.WaitAsync(cancellationToken);
        try
        {
            var wait = NextApiRequestUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
            NextApiRequestUtc = DateTime.UtcNow.AddMilliseconds(500);
            return await httpClient.SendAsync(request, cancellationToken);
        }
        finally
        {
            ApiThrottle.Release();
        }
    }

    private async Task<SkydropxProviderException> CrearErrorAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Skydropx respondió HTTP {StatusCode}: {Details}", (int)response.StatusCode, details);

        // 401/403: nuestras credenciales fallaron; 429: límite de tasa. Ambos son indisponibilidad (503)
        // desde el punto de vista del cliente. El resto de fallos del proveedor se reportan como 502.
        var statusCode = (int)response.StatusCode is 401 or 403 or 429 ? 503 : 502;
        if ((int)response.StatusCode == 401)
            cache.Remove(TokenCacheKey); // el token cacheado pudo ser revocado; no reutilizarlo hasta que expire

        return new SkydropxProviderException(fallback, statusCode);
    }
}

public sealed class SkydropxProviderException(string message, int statusCode = 502)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class SkydropxConfigurationException(string message)
    : Exception(message);

internal static class SkydropxSettings
{
    /// <summary>
    /// Lee un decimal de configuración con cultura invariante. Un valor mal escrito (por ejemplo "0,5" o
    /// "abc" en una variable de entorno) produce una SkydropxConfigurationException con el nombre de la
    /// clave, en lugar de un InvalidOperationException genérico del binder.
    /// </summary>
    public static decimal ReadDecimal(IConfiguration configuration, string key, bool required = true)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (required)
                throw new SkydropxConfigurationException($"Configura {key}.");
            return 0m;
        }

        // Sin AllowThousands: con NumberStyles.Number "0,5" se leería como 5 (la coma sería separador de miles).
        if (!decimal.TryParse(raw.Trim(),
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new SkydropxConfigurationException($"{key} no es un número válido (usa punto decimal).");

        return value;
    }
}
