using System.Security.Claims;

namespace back.Payments;

public static class ProcessOrderEndpoint
{
    public static void MapProcessOrder(this WebApplication app)
    {
        app.MapPost("/process_order", async (
            ProcessOrderRequest request,
            ClaimsPrincipal user,
            MercadoPagoPaymentService mercadoPago) =>
        {
            // Solo se puede pagar a nombre propio (o un administrador).
            var idToken = int.TryParse(
                user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

            if (idToken != request.UsuarioId && !user.IsInRole("Admin"))
            {
                return Results.Json(
                    new { message = "No puedes pagar a nombre de otro usuario." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var resultado = await mercadoPago.ProcessAsync(request);

                if (resultado.Status != "approved")
                {
                    return Results.BadRequest(new
                    {
                        message = resultado.Message ?? "El pago no fue aprobado.",
                        status = resultado.Status,
                        status_detail = resultado.StatusDetail,
                        id = resultado.Id
                    });
                }

                return Results.Ok(new
                {
                    id = resultado.Id,
                    status = resultado.Status,
                    status_detail = resultado.StatusDetail
                });
            }
            catch (PaymentRequestException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .RequireAuthorization();
    }
}
