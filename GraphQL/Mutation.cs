using HotChocolate;
using back.Services.Auth;

namespace back.GraphQL;

public class Mutation
{
   
    public Task<string> Registrar(
        string nombre,
        string email,
        string password,
        string? rol,
        [Service] IAuthService auth)
    {
        return auth.RegistrarAsync(nombre, email, password);
    }


    public Task<AuthPayload> Login(
        string email,
        string password,
        [Service] IAuthService auth)
    {
        return auth.LoginAsync(email, password);
    }


    public Task<AuthPayload> LoginGoogle(
        string idToken,
        [Service] IAuthService auth)
    {
        return auth.LoginGoogleAsync(idToken);
    }
}