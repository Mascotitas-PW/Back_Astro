using System.Security.Claims;
using HotChocolate;
using back.Services.Auth;

namespace back.GraphQL;

// Validaciones compartidas por las mutaciones de pedidos.
public static class PedidoAuth
{
    // Solo el dueño del pedido (o un administrador) puede crearlo.
    public static void ValidarPropietario(ClaimsPrincipal user, int usuarioId)
    {
        if (usuarioId != AuthHelper.ObtenerUsuarioId(user)
            && !AuthHelper.EsAdmin(user))
        {
            throw new GraphQLException(
                "No puedes crear pedidos a nombre de otro usuario.");
        }
    }

    // Valida que haya productos y cantidades correctas, y combina repetidos.
    public static List<ItemPedidoInput> ValidarItems(List<ItemPedidoInput>? items)
    {
        if (items == null || items.Count == 0)
        {
            throw new GraphQLException("El pedido no tiene productos.");
        }

        if (items.Any(i => i.Cantidad <= 0))
        {
            throw new GraphQLException(
                "Todas las cantidades deben ser mayores a 0.");
        }

        return items
            .GroupBy(i => i.ProductoId)
            .Select(g => new ItemPedidoInput(
                g.Key,
                g.Sum(x => x.Cantidad)))
            .ToList();
    }

    // Misma regla que el front: nombre, dirección y teléfono de 10 dígitos.
    public static (string Nombre, string Direccion, string Telefono) ValidarDatosEntrega(
        string? nombreCompleto,
        string? direccionEntrega,
        string? telefono)
    {
        var nombre = (nombreCompleto ?? "").Trim();
        var direccion = (direccionEntrega ?? "").Trim();
        var telefonoLimpio = new string(
            (telefono ?? "").Where(char.IsDigit).ToArray());

        if (nombre.Length < 3)
        {
            throw new GraphQLException("Escribe tu nombre completo.");
        }

        if (direccion.Length < 5)
        {
            throw new GraphQLException(
                "Escribe una dirección de entrega válida.");
        }

        if (telefonoLimpio.Length != 10)
        {
            throw new GraphQLException("El teléfono debe tener 10 dígitos.");
        }

        return (nombre, direccion, telefonoLimpio);
    }
}