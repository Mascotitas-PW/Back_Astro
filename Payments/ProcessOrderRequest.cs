using System.Text.Json;
namespace back.Payments;

public sealed record ProcessOrderRequest(
    string? Token,
    string? PaymentMethodId,
    int Installments,
    int UsuarioId,
    List<ProcessOrderItem>? Items,
    JsonElement? IssuerId,
    PaymentPayer? Payer);

public sealed record ProcessOrderItem(int ProductoId, int Cantidad);

public sealed record PaymentPayer(string? Email, PaymentIdentification? Identification);

public sealed record PaymentIdentification(string? Type, string? Number);

public sealed record PaymentResult(string Status, long? Id, string? StatusDetail, string? Message);

public sealed class PaymentRequestException(string message) : Exception(message);