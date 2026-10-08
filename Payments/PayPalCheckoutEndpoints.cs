using System.Security.Claims;
using back.Models;

public static class PayPalCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapPayPalCheckout(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/paypal-api/checkout/orders");
        group.MapPost("/create", CreateAsync);
        group.MapPost("/capture", CaptureAsync);
        return endpoints;
    }

    public static bool TryGetAuthenticatedUserId(ClaimsPrincipal principal, out int usuarioId)
    {
        usuarioId = 0;
        if (principal.Identity?.IsAuthenticated != true)
            return false;

        var claim = principal.FindFirst(ClaimTypes.NameIdentifier) ??
                    principal.FindFirst("sub") ??
                    principal.FindFirst("usuarioId");
        return claim is not null && int.TryParse(claim.Value, out usuarioId) && usuarioId > 0;
    }

    private static async Task<IResult> CreateAsync(
        PayPalCreateOrderRequest request,
        HttpContext httpContext,
        PayPalPaymentService payments)
    {
        if (!TryGetAuthenticatedUserId(httpContext.User, out var usuarioId))
            return Results.Json(new { error = "unauthorized", message = "Se requiere autenticación." }, statusCode: 401);
        if (request.UsuarioId != usuarioId)
            return Results.Json(new { error = "forbidden", message = "usuarioId no coincide con el usuario autenticado." }, statusCode: 403);

        try
        {
            var result = await payments.CreateOrderAsync(usuarioId, request);
            return Results.Ok(new { orderId = result.OrderId });
        }
        catch (PayPalRequestException exception)
        {
            return Results.BadRequest(new { error = "validation_error", message = exception.Message });
        }
        catch (PayPalCheckoutException exception)
        {
            return Results.Json(new { error = ErrorCodeForStatus(exception.StatusCode), message = exception.Message }, statusCode: exception.StatusCode);
        }
        catch (PayPalConfigurationException)
        {
            return Results.Json(new { error = "payment_not_configured", message = "PayPal no está configurado." }, statusCode: 503);
        }
        catch (HttpRequestException)
        {
            return Results.Json(new { error = "paypal_error", message = "PayPal no está disponible." }, statusCode: 502);
        }
        catch (TaskCanceledException)
        {
            return Results.Json(new { error = "paypal_error", message = "PayPal agotó el tiempo de espera." }, statusCode: 502);
        }
        catch
        {
            return Results.Json(new { error = "internal_error", message = "No se pudo crear la orden." }, statusCode: 500);
        }
    }

    private static async Task<IResult> CaptureAsync(
        PayPalCaptureRequest request,
        HttpContext httpContext,
        PayPalPaymentService payments)
    {
        if (!TryGetAuthenticatedUserId(httpContext.User, out var usuarioId))
            return Results.Json(new { error = "unauthorized", message = "Se requiere autenticación." }, statusCode: 401);

        try
        {
            var result = await payments.CaptureOrderAsync(request.OrderId ?? string.Empty, usuarioId);
            return Results.Ok(new { status = result.Status, captureId = result.CaptureId });
        }
        catch (PayPalRequestException exception)
        {
            return Results.BadRequest(new { error = "validation_error", message = exception.Message });
        }
        catch (PayPalCheckoutException exception)
        {
            return Results.Json(new { error = ErrorCodeForStatus(exception.StatusCode), message = exception.Message }, statusCode: exception.StatusCode);
        }
        catch (PayPalConfigurationException)
        {
            return Results.Json(new { error = "payment_not_configured", message = "PayPal no está configurado." }, statusCode: 503);
        }
        catch (HttpRequestException)
        {
            return Results.Json(new { error = "paypal_error", message = "PayPal no está disponible." }, statusCode: 502);
        }
        catch (TaskCanceledException)
        {
            return Results.Json(new { error = "paypal_error", message = "PayPal agotó el tiempo de espera." }, statusCode: 502);
        }
        catch
        {
            return Results.Json(new { error = "internal_error", message = "No se pudo capturar la orden." }, statusCode: 500);
        }
    }

    private static string ErrorCodeForStatus(int statusCode) => statusCode switch
    {
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not_found",
        409 => "conflict",
        _ => "paypal_error"
    };
}