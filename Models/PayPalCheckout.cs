namespace back.Models;

public sealed class PayPalCheckout
{
    public string OrderId { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public int UsuarioId { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public decimal MerchandiseAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "MXN";
    public string Status { get; set; } = "CREATED";
    public string? CaptureId { get; set; }
    public int? PedidoId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record PayPalCheckoutItem(int ProductoId, int Cantidad, decimal PrecioUnitario);

public sealed record PayPalCreateOrderRequest(int UsuarioId, List<PayPalOrderItemRequest>? Items);

public sealed record PayPalOrderItemRequest(int ProductoId, int Cantidad);

public sealed record PayPalCaptureRequest(string? OrderId);

public sealed record PayPalOrderResult(string OrderId);

public sealed record PayPalCaptureResult(string Status, string? CaptureId);

public sealed class PayPalRequestException(string message) : Exception(message);

public sealed class PayPalCheckoutException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class PayPalConfigurationException(string message) : Exception(message);