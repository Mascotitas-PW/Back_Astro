using Microsoft.AspNetCore.Diagnostics;

namespace back.Http;

/// <summary>
/// Manejo global de excepciones no controladas: registra la excepción completa en el log y responde un
/// JSON genérico sin detalles internos.
///
/// Nota sobre CORS: CorsMiddleware aplica sus headers al iniciar la respuesta (OnStarting), así que la
/// respuesta de este handler los conserva siempre que corra dentro del pipeline con UseCors. Sin ningún
/// handler, en cambio, la excepción llega al servidor, que responde un 500 vacío SIN headers CORS; el
/// navegador lo reporta como "error CORS" y oculta el 500 real.
/// </summary>
public static class ApiErrorHandling
{
    public const string GenericMessage = "Ocurrió un error inesperado. Inténtalo nuevamente.";

    public static IApplicationBuilder UseApiExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("back.ApiErrors");

            var (status, message) = Describe(error);
            if (status >= 500)
                logger.LogError(error, "Error no controlado en {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                logger.LogWarning(error, "Solicitud rechazada ({Status}) en {Method} {Path}", status, context.Request.Method, context.Request.Path);

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { message });
        }));
    }

    // Nunca se devuelve el texto de la excepción: puede incluir rutas, SQL o configuración.
    internal static (int Status, string Message) Describe(Exception? error) => error switch
    {
        BadHttpRequestException bad => (bad.StatusCode, "La solicitud no es válida."),
        _ => (StatusCodes.Status500InternalServerError, GenericMessage)
    };
}
