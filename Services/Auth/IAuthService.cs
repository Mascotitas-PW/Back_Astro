namespace back.Services.Auth;

public interface IAuthService
{
    Task<string> RegistrarAsync(string nombre, string email, string password);
    Task<AuthPayload> LoginAsync(string email, string password);
}
