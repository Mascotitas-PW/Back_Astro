using System.Text.Json;

namespace back.Payments;

public sealed record CodigoPostalMexicoResponse(
    string CodigoPostal,
    string Estado,
    string EstadoAbreviado,
    string Ciudad,
    IReadOnlyList<string> Colonias);

public interface IMexicoPostalService
{
    Task<CodigoPostalMexicoResponse?> BuscarAsync(string codigoPostal, CancellationToken cancellationToken);
}

public sealed class MexicoPostalService(HttpClient httpClient, ILogger<MexicoPostalService> logger) : IMexicoPostalService
{
    public async Task<CodigoPostalMexicoResponse?> BuscarAsync(
        string codigoPostal,
        CancellationToken cancellationToken)
    {
        var cp = new string((codigoPostal ?? "").Where(char.IsDigit).ToArray());
        if (cp.Length != 5)
            return null;

        using var response = await httpClient.GetAsync(
            $"https://api.zippopotam.us/MX/{cp}", cancellationToken);
        // 404 = el código postal no existe. Cualquier otro fallo del servicio es un error del proveedor
        // (HttpRequestException → 502), no "código postal no encontrado".
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Respuesta inesperada del servicio de códigos postales.");

        if (!root.TryGetProperty("places", out var places) ||
            places.ValueKind != JsonValueKind.Array)
            return null;

        var colonias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string estado = "";
        string estadoAbreviado = "";

        foreach (var place in places.EnumerateArray())
        {
            if (place.TryGetProperty("place name", out var placeName) &&
                placeName.ValueKind == JsonValueKind.String)
            {
                var colonia = placeName.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(colonia))
                    colonias.Add(colonia);
            }

            if (string.IsNullOrWhiteSpace(estado) &&
                place.TryGetProperty("state", out var state) &&
                state.ValueKind == JsonValueKind.String)
                estado = state.GetString()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(estadoAbreviado) &&
                place.TryGetProperty("state abbreviation", out var abbreviation) &&
                abbreviation.ValueKind == JsonValueKind.String)
                estadoAbreviado = abbreviation.GetString()?.Trim() ?? "";
        }

        if (colonias.Count == 0)
            return null;

        var listaColonias = colonias.ToArray();
        // La ciudad viene de Nominatim, que es opcional: si falla o tarda, se usa el estado en lugar de
        // convertir una caída de ese servicio en un error de la cotización.
        string? ciudadEncontrada = null;
        try
        {
            ciudadEncontrada = await BuscarCiudadAsync(cp, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "No se pudo resolver la ciudad del código postal {CodigoPostal}; se usará el estado.", cp);
        }

        var ciudad = ciudadEncontrada ?? estado;
        return new CodigoPostalMexicoResponse(cp, estado, estadoAbreviado, ciudad, listaColonias);
    }

    private async Task<string?> BuscarCiudadAsync(
        string codigoPostal,
        CancellationToken cancellationToken)
    {
        using var searchRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://nominatim.openstreetmap.org/search?" +
            "postalcode=" + Uri.EscapeDataString(codigoPostal) +
            "&countrycodes=mx&format=jsonv2&addressdetails=1&accept-language=es&limit=5");
        searchRequest.Headers.TryAddWithoutValidation("User-Agent", "Mascotitas-Backend/1.0");

        using var searchResponse = await httpClient.SendAsync(searchRequest, cancellationToken);
        if (!searchResponse.IsSuccessStatusCode)
            return null;

        using var searchDocument = await JsonDocument.ParseAsync(
            await searchResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (searchDocument.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var place in searchDocument.RootElement.EnumerateArray())
        {
            var addressCity = LeerDireccion(place, "city", "town", "municipality", "village", "county", "borough");
            if (!string.IsNullOrWhiteSpace(addressCity))
                return addressCity;

            if (!place.TryGetProperty("lat", out var latValue) ||
                !place.TryGetProperty("lon", out var lonValue) ||
                latValue.ValueKind != JsonValueKind.String ||
                lonValue.ValueKind != JsonValueKind.String)
                continue;

            var ciudadInvertida = await BuscarCiudadInversaAsync(
                latValue.GetString(), lonValue.GetString(), cancellationToken);
            if (!string.IsNullOrWhiteSpace(ciudadInvertida))
                return ciudadInvertida;
        }

        return null;
    }

    private async Task<string?> BuscarCiudadInversaAsync(
        string? latitud,
        string? longitud,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(latitud) || string.IsNullOrWhiteSpace(longitud))
            return null;

        using var reverseRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://nominatim.openstreetmap.org/reverse?" +
            "lat=" + Uri.EscapeDataString(latitud) +
            "&lon=" + Uri.EscapeDataString(longitud) +
            "&format=jsonv2&addressdetails=1&accept-language=es&zoom=10");
        reverseRequest.Headers.TryAddWithoutValidation("User-Agent", "Mascotitas-Backend/1.0");

        using var reverseResponse = await httpClient.SendAsync(reverseRequest, cancellationToken);
        if (!reverseResponse.IsSuccessStatusCode)
            return null;

        using var reverseDocument = await JsonDocument.ParseAsync(
            await reverseResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return LeerDireccion(reverseDocument.RootElement, "city", "town", "municipality", "village", "county", "borough");
    }

    private static string? LeerDireccion(JsonElement place, params string[] propiedades)
    {
        if (!place.TryGetProperty("address", out var address) ||
            address.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var propiedad in propiedades)
        {
            if (address.TryGetProperty(propiedad, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                var texto = value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(texto))
                    return texto;
            }
        }

        return null;
    }
}
