namespace back.Payments.PayPal;

public sealed record PayPalCreateOrderRequest(
    int UsuarioId,
    List<PayPalOrderItem>? Items,
    string? NombreCompleto,
    string? DireccionEntrega,
    string? Telefono,
    double? Latitud,
<<<<<<< HEAD
    double? Longitud);
=======
    double? Longitud,
    Guid? CotizacionEnvioId,
    string? RateId,
    string? CodigoPostal,
    string? Estado,
    string? Ciudad,
    string? Colonia);
>>>>>>> 95a2c0e (envios)

public sealed record PayPalOrderItem(int ProductoId, int Cantidad);

public sealed record PayPalOrderResponse(string Id, string Status);

public sealed record PayPalCaptureResponse(string Id, string Status, decimal Amount, string Currency);

public sealed record PayPalOrderCaptureResult(
    PayPalCaptureResponse Capture,
    back.Models.Pedido Pedido);
