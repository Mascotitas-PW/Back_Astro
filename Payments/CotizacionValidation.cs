using back.GraphQL;
using back.Models;

namespace back.Payments;

public sealed record CrearCotizacionRequest(
    List<ItemPedidoInput>? Items,
    string? CodigoPostal,
    string? Estado,
    string? Ciudad,
    string? Colonia);

public sealed record CotizacionResponse(Guid Id, IReadOnlyList<TarifaEnvio> Tarifas, DateTime VenceUtc);

public sealed record CotizacionValida(
    string CodigoPostal,
    string Estado,
    string Ciudad,
    string Colonia,
    IReadOnlyList<ItemPedidoInput> Items);

public static class CotizacionValidation
{
    public const int MaxLineas = 100;
    public const int MaxCantidad = 1000;
    public const int MaxTextoDireccion = 120;

    /// <summary>Valida el payload y devuelve un mensaje útil para el cliente (400) si es inválido.</summary>
    public static (CotizacionValida? Valida, string? Error) Validar(CrearCotizacionRequest? request)
    {
        if (request is null)
            return (null, "El cuerpo de la solicitud es obligatorio.");

        var errores = new List<string>();

        var cp = new string((request.CodigoPostal ?? "").Where(char.IsDigit).ToArray());
        if (cp.Length != 5)
            errores.Add("El código postal debe tener 5 dígitos.");

        var estado = (request.Estado ?? "").Trim();
        var ciudad = (request.Ciudad ?? "").Trim();
        var colonia = (request.Colonia ?? "").Trim();
        ValidarTexto("estado", estado, errores);
        ValidarTexto("ciudad", ciudad, errores);
        ValidarTexto("colonia", colonia, errores);

        var items = new List<ItemPedidoInput>();
        if (request.Items is null || request.Items.Count == 0)
        {
            errores.Add("El carrito está vacío.");
        }
        else if (request.Items.Count > MaxLineas)
        {
            errores.Add($"El carrito no puede tener más de {MaxLineas} productos distintos.");
        }
        else
        {
            var validas = request.Items.Where(i => i is not null && i.ProductoId >= 1).ToList();
            if (validas.Count != request.Items.Count)
                errores.Add("Hay un producto con identificador inválido.");

            foreach (var grupo in validas.GroupBy(i => i.ProductoId))
            {
                // long evita OverflowException (→ 500) al sumar cantidades enormes.
                var total = grupo.Sum(i => (long)i.Cantidad);
                if (grupo.Any(i => i.Cantidad < 1) || total > MaxCantidad)
                {
                    errores.Add($"La cantidad del producto {grupo.Key} debe estar entre 1 y {MaxCantidad}.");
                    continue;
                }

                items.Add(new ItemPedidoInput(grupo.Key, (int)total));
            }
        }

        if (errores.Count > 0)
            return (null, string.Join(" ", errores));

        return (new CotizacionValida(cp, estado, ciudad, colonia, items), null);
    }

    private static void ValidarTexto(string campo, string valor, List<string> errores)
    {
        if (valor.Length < 2)
            errores.Add($"Indica el campo {campo}.");
        else if (valor.Length > MaxTextoDireccion)
            errores.Add($"El campo {campo} no puede superar {MaxTextoDireccion} caracteres.");
    }
}
