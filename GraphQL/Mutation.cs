using HotChocolate;
using back.Services.Auth;

namespace back.GraphQL;

// Mutaciones de cuenta. Las de pedidos están en PedidoMutation.cs
// y las de productos en ProductMutation.cs (ambas extienden esta clase).
public class Mutation
{
    // REGISTRAR USUARIO
    // IAuthService ignora el rol enviado y crea siempre un Cliente.
    public Task<string> Registrar(
        string nombre,
        string email,
        string password,
        string? rol,
        [Service] IAuthService auth)
    {
        return auth.RegistrarAsync(nombre, email, password);
    }

    // INICIAR SESIÓN
    public Task<AuthPayload> Login(
        string email,
        string password,
        [Service] IAuthService auth)
    {
        return auth.LoginAsync(email, password);
    }

    // INICIAR SESIÓN CON GOOGLE
    public Task<AuthPayload> LoginGoogle(
        string idToken,
        [Service] IAuthService auth)
    {
        return auth.LoginGoogleAsync(idToken);
    }
}