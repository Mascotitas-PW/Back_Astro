using back.Models;

namespace back.Services.Auth;

public interface IJwtService
{
    string GenerateToken(User usuario);
}
