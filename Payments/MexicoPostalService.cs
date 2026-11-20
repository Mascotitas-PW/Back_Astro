using System.Text.Json;

namespace back.Payments;

public sealed record CodigoPostalMexicoResponse(
    string CodigoPostal,
    string Estado,
    string EstadoAbreviado,
    string Ciudad,
    IReadOnlyList<string> Colonias);

public sealed class MexicoPostalService(HttpClient httpClient)
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
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;

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
        var ciudad = listaColonias.Length == 1 ? listaColonias[0] : estado;
        return new CodigoPostalMexicoResponse(cp, estado, estadoAbreviado, ciudad, listaColonias);
    }
}
