using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using back.GraphQL;
using back.Services.Pedidos;

namespace back.Payments.PayPal;

public sealed class PayPalPaymentService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PayPalPaymentService> logger,
    PedidoService pedidos)
{
    public async Task<PayPalOrderResponse> CreateOrderAsync(
        PayPalCreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var amount = await CalculateAmountAsync(request, cancellationToken);
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var currency = configuration["PayPal:Currency"] ?? "MXN";

        var order = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    custom_id = $"checkout-{request.UsuarioId}-{Guid.NewGuid():N}",
                    amount = new
                    {
                        currency_code = currency,
                        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v2/checkout/orders")
        {
            Content = JsonContent.Create(order)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add("PayPal-Request-Id", Guid.NewGuid().ToString());

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreatePayPalExceptionAsync(response, "No se pudo crear la orden de PayPal.");

        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var id = ReadString(body.RootElement, "id");
        var status = ReadString(body.RootElement, "status");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("PayPal devolvió una orden incompleta.");

        return new PayPalOrderResponse(id, status);
    }

    public async Task<PayPalCaptureResponse> CaptureOrderAsync(
        string orderId,
        PayPalCreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new PaymentRequestException("El identificador de la orden de PayPal es obligatorio.");

        var expectedAmount = await CalculateAmountAsync(request, cancellationToken);
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var expectedCurrency = configuration["PayPal:Currency"] ?? "MXN";
        await ValidateOrderBeforeCaptureAsync(
            orderId,
            request.UsuarioId,
            expectedAmount,
            expectedCurrency,
            accessToken,
            cancellationToken);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture")
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Add("PayPal-Request-Id", Guid.NewGuid().ToString());

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreatePayPalExceptionAsync(response, "No se pudo capturar la orden de PayPal.");

        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = body.RootElement;
        var capture = new PayPalCaptureResponse(
            ReadString(root, "id") ?? orderId,
            ReadString(root, "status") ?? "UNKNOWN",
            ReadCaptureAmount(root),
            ReadCaptureCurrency(root) ?? configuration["PayPal:Currency"] ?? "MXN");

        if (capture.Status != "COMPLETED" ||
            capture.Amount != expectedAmount ||
            capture.Currency != expectedCurrency)
        {
            logger.LogWarning(
                "La captura de PayPal no coincide. OrderId {OrderId}; estado {Status}; importe recibido {ReceivedAmount}; importe esperado {ExpectedAmount}; moneda recibida {ReceivedCurrency}; moneda esperada {ExpectedCurrency}; usuario esperado {UsuarioId}.",
                orderId,
                capture.Status,
                capture.Amount,
                expectedAmount,
                capture.Currency,
                expectedCurrency,
                request.UsuarioId);
            throw new PaymentRequestException("El pago capturado no coincide con el pedido esperado.");
        }

        return capture;
    }

    // Valida entrega, stock y total; captura el pago y, solo si todo salió bien,
    // guarda el pedido (PedidoService también publica los eventos en vivo).
    public async Task<PayPalOrderCaptureResult> CaptureAndCreateOrderAsync(
        string orderId,
        PayPalCreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        PayPalCaptureResponse? capture = null;

        var pedido = await pedidos.CrearAsync(
            new NuevoPedido(
                request.UsuarioId,
                ToItems(request.Items),
                request.NombreCompleto,
                request.DireccionEntrega,
                request.Telefono,
                "Pendiente"),
            async _ => { capture = await CaptureOrderAsync(orderId, request, cancellationToken); },
            cancellationToken);

        return new PayPalOrderCaptureResult(capture!, pedido);
    }

    private async Task ValidateOrderBeforeCaptureAsync(
        string orderId,
        int usuarioId,
        decimal expectedAmount,
        string expectedCurrency,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreatePayPalExceptionAsync(response, "No se pudo verificar la orden de PayPal.");

        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = body.RootElement;
        var orderStatus = ReadString(root, "status");
        var customId = ReadCustomId(root);
        var orderAmount = ReadPurchaseUnitAmount(root);
        var orderCurrency = ReadPurchaseUnitCurrency(root);
        if (orderStatus != "APPROVED" ||
            orderAmount != expectedAmount ||
            orderCurrency != expectedCurrency ||
            customId?.StartsWith($"checkout-{usuarioId}-", StringComparison.Ordinal) != true)
        {
            logger.LogWarning(
                "La orden PayPal no coincide antes de capturar. OrderId {OrderId}; estado {Status}; importe recibido {ReceivedAmount}; importe esperado {ExpectedAmount}; moneda recibida {ReceivedCurrency}; moneda esperada {ExpectedCurrency}; custom_id {CustomId}; usuario esperado {UsuarioId}.",
                orderId,
                orderStatus,
                orderAmount,
                expectedAmount,
                orderCurrency,
                expectedCurrency,
                customId,
                usuarioId);
            throw new PaymentRequestException("La orden de PayPal no coincide con el pedido esperado.");
        }
    }

    // El total siempre lo calcula el servidor, con la misma lógica que el pedido final.
    private Task<decimal> CalculateAmountAsync(
        PayPalCreateOrderRequest request,
        CancellationToken cancellationToken) =>
        pedidos.CalcularTotalAsync(request.UsuarioId, ToItems(request.Items), cancellationToken);

    private static List<ItemPedidoInput>? ToItems(List<PayPalOrderItem>? items) =>
        items?.Select(i => new ItemPedidoInput(i.ProductoId, i.Cantidad)).ToList();

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = configuration["PAYPAL_CLIENT_ID"];
        var clientSecret = configuration["PAYPAL_CLIENT_SECRET"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("PAYPAL_CLIENT_ID y PAYPAL_CLIENT_SECRET son obligatorios.");

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            })
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreatePayPalExceptionAsync(response, "No se pudo autenticar con PayPal.");

        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var token = ReadString(body.RootElement, "access_token");
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("PayPal no devolvió un token de acceso.")
            : token;
    }

    private async Task<InvalidOperationException> CreatePayPalExceptionAsync(
        HttpResponseMessage response,
        string message)
    {
        var details = await response.Content.ReadAsStringAsync();
        logger.LogWarning("PayPal rechazó {RequestUri}. HTTP {StatusCode}. Respuesta: {Details}",
            response.RequestMessage?.RequestUri, (int)response.StatusCode, details);
        return new InvalidOperationException(message);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal ReadCaptureAmount(JsonElement root)
    {
        var capture = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("captures")[0];
        return decimal.Parse(
            capture.GetProperty("amount").GetProperty("value").GetString() ?? "0",
            CultureInfo.InvariantCulture);
    }

    private static string? ReadCaptureCurrency(JsonElement root) =>
        root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("captures")[0]
            .GetProperty("amount").GetProperty("currency_code").GetString();

    private static string? ReadCustomId(JsonElement root) =>
        root.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() > 0
            ? ReadString(units[0], "custom_id")
            : null;

    private static decimal? ReadPurchaseUnitAmount(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0)
            return null;

        var amount = units[0].GetProperty("amount").GetProperty("value").GetString();
        return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? ReadPurchaseUnitCurrency(JsonElement root) =>
        root.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() > 0
            ? units[0].GetProperty("amount").GetProperty("currency_code").GetString()
            : null;
}
