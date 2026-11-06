namespace back.Payments.PayPal;

public sealed record PayPalCreateOrderRequest(int UsuarioId, List<PayPalOrderItem>? Items);

public sealed record PayPalOrderItem(int ProductoId, int Cantidad);

public sealed record PayPalOrderResponse(string Id, string Status);

public sealed record PayPalCaptureResponse(string Id, string Status, decimal Amount, string Currency);
