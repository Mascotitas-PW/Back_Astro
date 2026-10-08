using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace back.Payments;

public sealed class MercadoPagoPaymentService(
    HttpClient httpClient,
    IConfiguration configuration,
    AppDbContext context,
    ILogger<MercadoPagoPaymentService> logger)
{
    public async Task<PaymentResult> ProcessAsync(ProcessOrderRequest request)
    {
        var accessToken = configuration["MP_TOKEN"];
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("MP_TOKEN is missing.");

        if (string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.PaymentMethodId) ||
            request.Installments < 1 || request.UsuarioId < 1 ||
            request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentRequestException("Faltan datos válidos para procesar el pago.");
        }

        if (request.Items.Any(item => item.ProductoId < 1 || item.Cantidad < 1))
            throw new PaymentRequestException("Los productos y cantidades del pedido no son válidos.");

        var usuario = await context.Usuarios
            .Where(user => user.Id == request.UsuarioId)
            .Select(user => new { user.Id, user.email })
            .SingleOrDefaultAsync();
        if (usuario is null)
            throw new PaymentRequestException("El usuario indicado no existe.");

        var items = request.Items
            .GroupBy(item => item.ProductoId)
            .Select(group => new ProcessOrderItem(group.Key, group.Sum(item => item.Cantidad)))
            .ToList();
        var productIds = items.Select(item => item.ProductoId).ToList();
        var products = await context.Productos
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        decimal amount = 0;
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductoId, out var product))
                throw new PaymentRequestException($"El producto {item.ProductoId} no existe.");
            if (product.Stock < item.Cantidad)
                throw new PaymentRequestException($"Stock insuficiente para '{product.Nombre}'.");

            amount += product.Precio * item.Cantidad;
        }

        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var currency = configuration["MERCADOPAGO_CURRENCY"] ?? "MXN";

        // 1. Construcción del Payer
        var payerData = new Dictionary<string, object?>
        {
            ["email"] = usuario.email
        };

        if (request.Payer?.Identification is { } identification &&
            !string.IsNullOrWhiteSpace(identification.Type) &&
            !string.IsNullOrWhiteSpace(identification.Number))
        {
            payerData["identification"] = new
            {
                type = identification.Type,
                number = identification.Number
            };
        }

        // 2. Construcción del cuerpo principal del pago
        var payment = new Dictionary<string, object?>
        {
            ["transaction_amount"] = amount,
            ["token"] = request.Token,
            ["description"] = $"Pedido de usuario {usuario.Id}",
            ["installments"] = request.Installments,
            ["payment_method_id"] = request.PaymentMethodId,
            ["payer"] = payerData,
            ["external_reference"] = $"checkout-{usuario.Id}-{Guid.NewGuid():N}",
            ["metadata"] = new
            {
                usuario_id = usuario.Id,
                items = items.Select(item => new
                {
                    producto_id = item.ProductoId,
                    cantidad = item.Cantidad
                })
            }
        };

        // 3. Extracción limpia de issuer_id
        if (request.IssuerId is { } issuerElement && issuerElement.ValueKind != JsonValueKind.Null)
        {
            if (issuerElement.ValueKind == JsonValueKind.Number && issuerElement.TryGetInt64(out var issuerLong))
                payment["issuer_id"] = issuerLong;
            else if (issuerElement.ValueKind == JsonValueKind.String)
                payment["issuer_id"] = issuerElement.GetString();
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/v1/payments")
        {
            Content = JsonContent.Create(payment)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await httpClient.SendAsync(message);
        if (!response.IsSuccessStatusCode)
        {
            var rawJson = await response.Content.ReadAsStringAsync();
            var error = await ReadApiErrorAsync(response);

            logger.LogWarning(
                "Mercado Pago rechazó el pago. HTTP {StatusCode}; código {ErrorCode}; detalle {ErrorMessage}. Respuesta API: {RawJson}",
                (int)response.StatusCode,
                error.Code,
                error.Message,
                rawJson);

            return new PaymentResult(
                "rejected",
                null,
                error.Code,
                "Mercado Pago rechazó el pago. Consulta los logs del backend para ver el motivo.");
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var status = ReadString(root, "status");
        var statusDetail = ReadString(root, "status_detail");
        var paymentId = root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id)
            ? id
            : (long?)null;
        var paidAmount = root.TryGetProperty("transaction_amount", out var amountElement) &&
                         amountElement.TryGetDecimal(out var parsedAmount)
            ? parsedAmount
            : (decimal?)null;
        var paidCurrency = ReadString(root, "currency_id");

        if (status != "approved" || paidAmount != amount || paidCurrency != currency || paymentId is null)
            return new PaymentResult("rejected", paymentId, statusDetail,
                "El pago no fue aprobado por el importe esperado.");

        return new PaymentResult("approved", paymentId, statusDetail, null);
    }

    public async Task<bool> IsApprovedForOrderAsync(
        long paymentId,
        int usuarioId,
        IReadOnlyDictionary<int, int> expectedItems,
        decimal expectedAmount)
    {
        var accessToken = configuration["MP_TOKEN"];
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("MP_TOKEN is missing.");

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.mercadopago.com/v1/payments/{paymentId}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(message);
        if (!response.IsSuccessStatusCode)
        {
            var rawJson = await response.Content.ReadAsStringAsync();
            logger.LogWarning("Falló la verificación del pago {PaymentId}. HTTP {StatusCode}. Respuesta API: {RawJson}", paymentId, (int)response.StatusCode, rawJson);
            return false;
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var paidAmount = root.TryGetProperty("transaction_amount", out var amountElement) &&
                         amountElement.TryGetDecimal(out var parsedAmount)
            ? parsedAmount
            : (decimal?)null;
        var currency = configuration["MERCADOPAGO_CURRENCY"] ?? "MXN";
        var externalReference = ReadString(root, "external_reference");
        if (!root.TryGetProperty("metadata", out var metadata) ||
            !metadata.TryGetProperty("usuario_id", out var metadataUserId) ||
            !metadataUserId.TryGetInt32(out var parsedUserId) || parsedUserId != usuarioId ||
            !metadata.TryGetProperty("items", out var metadataItems) ||
            metadataItems.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var paidItems = new Dictionary<int, int>();
        foreach (var item in metadataItems.EnumerateArray())
        {
            if (!item.TryGetProperty("producto_id", out var productIdElement) ||
                !productIdElement.TryGetInt32(out var productId) ||
                !item.TryGetProperty("cantidad", out var quantityElement) ||
                !quantityElement.TryGetInt32(out var quantity) ||
                !paidItems.TryAdd(productId, quantity))
            {
                return false;
            }
        }

        return ReadString(root, "status") == "approved" &&
               paidAmount == decimal.Round(expectedAmount, 2, MidpointRounding.AwayFromZero) &&
               ReadString(root, "currency_id") == currency &&
               expectedItems.Count == paidItems.Count &&
               expectedItems.All(item => paidItems.TryGetValue(item.Key, out var quantity) && quantity == item.Value) &&
               externalReference?.StartsWith($"checkout-{usuarioId}-", StringComparison.Ordinal) == true;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<(string? Code, string? Message)> ReadApiErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = body.RootElement;
            var code = ReadString(root, "error");
            var message = ReadString(root, "message");

            if (root.TryGetProperty("cause", out var causes) &&
                causes.ValueKind == JsonValueKind.Array && causes.GetArrayLength() > 0)
            {
                var cause = causes[0];
                var causeCode = ReadString(cause, "code");
                var description = ReadString(cause, "description");
                code = string.Join("/", new[] { code, causeCode }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(description))
                    message = string.IsNullOrWhiteSpace(message) ? description : $"{message}: {description}";
            }

            return (code, message);
        }
        catch (JsonException)
        {
            return (null, "La respuesta de Mercado Pago no tenía un formato JSON válido.");
        }
    }
}