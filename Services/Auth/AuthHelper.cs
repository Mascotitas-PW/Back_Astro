using System.Security.Claims;
using HotChocolate;

namespace back.Services.Auth;

public static class AuthHelper
{
    public static int ObtenerUsuarioId(ClaimsPrincipal user)
    {
        var valor = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(valor, out var id))
            throw new GraphQLException("Sesión no válida.");

        return id;
    }

    public static bool EsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin");

    public static void ExigirAdminOPropietario(ClaimsPrincipal user, int usuarioId)
    {
        if (!EsAdmin(user) && ObtenerUsuarioId(user) != usuarioId)
            throw new GraphQLException("No tienes permiso para ver esta información.");
    }
}
