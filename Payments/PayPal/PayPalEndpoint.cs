using System.Security.Claims;

namespace back.Payments.PayPal;

public static class PayPalEndpoint
{
	public static void MapPayPal(this WebApplication app)
	{
		var group = app.MapGroup("/api").RequireAuthorization();

		group.MapPost("/orders", async (
			PayPalCreateOrderRequest request,
			ClaimsPrincipal user,
			PayPalPaymentService service,
			ILogger<PayPalPaymentService> logger,
			CancellationToken cancellationToken) =>
		{
			var userId = int.TryParse(
				user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
			if (userId != request.UsuarioId && !user.IsInRole("Admin"))
				return Results.Json(new { message = "No puedes pagar a nombre de otro usuario." },
					statusCode: StatusCodes.Status403Forbidden);

			try
			{
				return Results.Ok(await service.CreateOrderAsync(request, cancellationToken));
			}
			catch (PaymentRequestException ex)
			{
				return Results.BadRequest(new { message = ex.Message });
			}
			catch (InvalidOperationException ex)
			{
				logger.LogError(ex, "Error al crear la orden de PayPal para el usuario {UsuarioId}.", request.UsuarioId);
				return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
			}
		});

		group.MapPost("/orders/{orderId}/capture", async (
			string orderId,
			PayPalCreateOrderRequest request,
			ClaimsPrincipal user,
			PayPalPaymentService service,
			ILogger<PayPalPaymentService> logger,
			CancellationToken cancellationToken) =>
		{
			var userId = int.TryParse(
				user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
			if (userId != request.UsuarioId && !user.IsInRole("Admin"))
				return Results.Json(new { message = "No puedes capturar el pago de otro usuario." },
					statusCode: StatusCodes.Status403Forbidden);

			try
			{
				var result = await service.CaptureAndCreateOrderAsync(orderId, request, cancellationToken);
				return result.Capture.Status == "COMPLETED"
					? Results.Ok(result)
					: Results.BadRequest(new { message = "PayPal no completó el pago.", capture = result.Capture });
			}
			catch (PaymentRequestException ex)
			{
				return Results.BadRequest(new { message = ex.Message });
			}
			catch (InvalidOperationException ex)
			{
				logger.LogError(ex,
					"Error al capturar la orden de PayPal {OrderId} para el usuario {UsuarioId}.",
					orderId, request.UsuarioId);
				return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
			}
		});
	}
}
