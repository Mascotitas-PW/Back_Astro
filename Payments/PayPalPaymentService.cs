using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using back.Models;

public sealed class PayPalPaymentService(
    HttpClient httpClient,
    IConfiguration configuration,
    AppDbContext context)
{
    private const string Currency = "MXN";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PayPalOrderResult> CreateOrderAsync(int usuarioId, PayPalCreateOrderRequest request)
    {
        if (usuarioId < 1 || request.Items is null || request.Items.Count == 0)
            throw new PayPalRequestException("El pedido no contiene productos válidos.");
        if (!await context.Usuarios.AnyAsync(user => user.Id == usuarioId))
            throw new PayPalCheckoutException(401, "El usuario autenticado ya no existe.");
        if (request.Items.Any(item => item.ProductoId < 1 || item.Cantidad < 1))
            throw new PayPalRequestException("Los productos y cantidades no son válidos.");

        var items = request.Items
            .GroupBy(item => item.ProductoId)
            .Select(group => new { ProductoId = group.Key, Cantidad = group.Sum(item => item.Cantidad) })
            .ToList();
        var productIds = items.Select(item => item.ProductoId).ToArray();
        var products = await context.Productos
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        var checkoutItems = new List<PayPalCheckoutItem>();
        decimal merchandiseAmount = 0;
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductoId, out var product))
                throw new PayPalRequestException($"El producto {item.ProductoId} no existe.");
            if (product.Stock < item.Cantidad)
                throw new PayPalRequestException($"Stock insuficiente para '{product.Nombre}'.");

            merchandiseAmount += product.Precio * item.Cantidad;
            checkoutItems.Add(new PayPalCheckoutItem(product.Id, item.Cantidad, product.Precio));
        }

        merchandiseAmount = RoundAmount(merchandiseAmount);
        var shippingAmount = merchandiseAmount >= 500m ? 0m : 99m;
        var totalAmount = RoundAmount(merchandiseAmount + shippingAmount);
        var referenceId = Guid.NewGuid().ToString("N");
        var accessToken = await GetAccessTokenAsync();
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/v2/checkout/orders"))
        {
            Content = JsonContent.Create(new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = referenceId,
                        custom_id = referenceId,
                        amount = new
                        {
                            currency_code = Currency,
                            value = FormatAmount(totalAmount),
                            breakdown = new
                            {
                                item_total = new { currency_code = Currency, value = FormatAmount(merchandiseAmount) },
                                shipping = new { currency_code = Currency, value = FormatAmount(shippingAmount) }
                            }
                        },
                        items = checkoutItems.Select(item => new
                        {
                            name = products[item.ProductoId].Nombre,
                            quantity = item.Cantidad.ToString(),
                            unit_amount = new
                            {
                                currency_code = Currency,
                                value = FormatAmount(item.PrecioUnitario)
                            }
                        })
                    }
                }
            }, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString("N"));

        using var response = await httpClient.SendAsync(message);
        using var body = await ReadSuccessfulBodyAsync(response);
        var root = body.RootElement;
        var orderId = ReadString(root, "id");
        if (string.IsNullOrWhiteSpace(orderId) || ReadString(root, "status") != "CREATED")
            throw new PayPalCheckoutException(502, "PayPal devolvió una orden inválida.");

        context.PayPalCheckouts.Add(new PayPalCheckout
        {
            OrderId = orderId,
            ReferenceId = referenceId,
            UsuarioId = usuarioId,
            ItemsJson = JsonSerializer.Serialize(checkoutItems, JsonOptions),
            MerchandiseAmount = merchandiseAmount,
            ShippingAmount = shippingAmount,
            TotalAmount = totalAmount,
            Currency = Currency,
            Status = "CREATED",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return new PayPalOrderResult(orderId);
    }

    public async Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, int usuarioId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new PayPalRequestException("orderId es obligatorio.");

        var checkout = await context.PayPalCheckouts.SingleOrDefaultAsync(item => item.OrderId == orderId);
        if (checkout is null)
            throw new PayPalCheckoutException(404, "La orden de checkout no existe.");
        if (checkout.UsuarioId != usuarioId)
            throw new PayPalCheckoutException(403, "La orden no pertenece al usuario autenticado.");
        if (checkout.Status == "COMPLETED" && !string.IsNullOrWhiteSpace(checkout.CaptureId))
            return new PayPalCaptureResult("COMPLETED", checkout.CaptureId);

        var accessToken = await GetAccessTokenAsync();
        var remoteOrder = await GetOrderAsync(orderId, accessToken);
        ValidateRemoteOrder(remoteOrder.RootElement, checkout);
        var remoteStatus = ReadString(remoteOrder.RootElement, "status");

        JsonDocument captureBody;
        if (remoteStatus == "COMPLETED")
        {
            captureBody = remoteOrder;
        }
        else if (remoteStatus is "CREATED" or "APPROVED")
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                ApiUrl($"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", CreateCaptureRequestId(orderId));
            message.Content = JsonContent.Create(new { }, options: JsonOptions);
            using var response = await httpClient.SendAsync(message);
            captureBody = await ReadSuccessfulBodyAsync(response);
        }
        else
        {
            throw new PayPalCheckoutException(409, "La orden de PayPal no está disponible para captura.");
        }

        using (captureBody)
        {
            if (ReadString(captureBody.RootElement, "status") != "COMPLETED")
                throw new PayPalCheckoutException(409, "PayPal no confirmó la captura.");

            var (captureId, captureAmount, captureCurrency) = ReadCapture(captureBody.RootElement);
            if (string.IsNullOrWhiteSpace(captureId) || captureAmount != checkout.TotalAmount ||
                captureCurrency != checkout.Currency)
            {
                throw new PayPalCheckoutException(502, "El importe o la moneda capturados no coinciden con el checkout.");
            }

            checkout.Status = "COMPLETED";
            checkout.CaptureId = captureId;
            await context.SaveChangesAsync();
            return new PayPalCaptureResult("COMPLETED", captureId);
        }
    }

    public async Task<PayPalCheckout?> GetCapturedCheckoutAsync(string orderId, int usuarioId)
    {
        var checkout = await context.PayPalCheckouts.SingleOrDefaultAsync(item => item.OrderId == orderId);
        if (checkout is null || checkout.UsuarioId != usuarioId || checkout.Status != "COMPLETED" || checkout.PedidoId is not null)
            return null;
        return checkout;
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var clientId = configuration["PUBLIC_PP_CLIENT"];
        var clientSecret = configuration["PP_KEY_SECRET"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new PayPalConfigurationException("PayPal credentials are not configured.");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/v1/oauth2/token"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await httpClient.SendAsync(message);
        using var body = await ReadSuccessfulBodyAsync(response);
        var token = ReadString(body.RootElement, "access_token");
        return string.IsNullOrWhiteSpace(token)
            ? throw new PayPalCheckoutException(502, "PayPal no devolvió un token válido.")
            : token;
    }

    private async Task<JsonDocument> GetOrderAsync(string orderId, string accessToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            ApiUrl($"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(message);
        return await ReadSuccessfulBodyAsync(response);
    }

    private string ApiUrl(string path)
    {
        var environment = configuration["PAYPAL_ENVIRONMENT"] ?? "Sandbox";
        var baseUrl = environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : throw new PayPalConfigurationException("PAYPAL_ENVIRONMENT must be Sandbox or Live.");
        return $"{baseUrl}{path}";
    }

    private static void ValidateRemoteOrder(JsonElement root, PayPalCheckout checkout)
    {
        if (ReadString(root, "id") != checkout.OrderId ||
            !TryReadPurchaseUnit(root, out var purchaseUnit) ||
            ReadString(purchaseUnit, "custom_id") != checkout.ReferenceId ||
            !TryReadAmount(purchaseUnit, "amount", out var amount, out var currency) ||
            amount != checkout.TotalAmount || currency != checkout.Currency)
        {
            throw new PayPalCheckoutException(502, "La orden de PayPal no coincide con el checkout almacenado.");
        }
    }

    private static (string? Id, decimal? Amount, string? Currency) ReadCapture(JsonElement root)
    {
        if (!TryReadPurchaseUnit(root, out var purchaseUnit) ||
            !purchaseUnit.TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("captures", out var captures) ||
            captures.ValueKind != JsonValueKind.Array || captures.GetArrayLength() != 1)
        {
            return (null, null, null);
        }

        var capture = captures[0];
        if (ReadString(capture, "status") != "COMPLETED" ||
            !TryReadAmount(capture, "amount", out var amount, out var currency))
        {
            return (null, null, null);
        }
        return (ReadString(capture, "id"), amount, currency);
    }

    private static bool TryReadPurchaseUnit(JsonElement root, out JsonElement purchaseUnit)
    {
        purchaseUnit = default;
        if (!root.TryGetProperty("purchase_units", out var units) ||
            units.ValueKind != JsonValueKind.Array || units.GetArrayLength() != 1)
        {
            return false;
        }
        purchaseUnit = units[0];
        return true;
    }

    private static bool TryReadAmount(JsonElement element, string property, out decimal amount, out string? currency)
    {
        amount = 0;
        currency = null;
        if (!element.TryGetProperty(property, out var amountElement) ||
            !decimal.TryParse(ReadString(amountElement, "value"), System.Globalization.CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }
        currency = ReadString(amountElement, "currency_code");
        return currency is not null;
    }

    private static async Task<JsonDocument> ReadSuccessfulBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalCheckoutException(502, "PayPal rechazó la solicitud.");
        }
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new PayPalCheckoutException(502, "PayPal devolvió una respuesta inválida.");
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FormatAmount(decimal amount) =>
        RoundAmount(amount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static decimal RoundAmount(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string CreateCaptureRequestId(string orderId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"paypal-capture:{orderId}")))
            .ToLowerInvariant()[..32];
}