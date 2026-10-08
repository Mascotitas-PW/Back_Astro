
namespace back.Payments;
public static class ProcessOrderEndpoint
{
    public static void MapProcessOrder(this WebApplication app)
    {
        app.MapPost("/process_order", async (
            ProcessOrderRequest request,
            MercadoPagoPaymentService mercadoPago) =>
        {
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
                return Results.BadRequest(new
                {
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new
                {
                    message = ex.Message
                });
            }
        });
    }
}