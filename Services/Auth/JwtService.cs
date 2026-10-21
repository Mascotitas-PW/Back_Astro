using System.Security.Claims;
using System.Text;
using back.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace back.Services.Auth;

public class JwtService(IConfiguration configuration) : IJwtService
{
    public string GenerateToken(User usuario)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Falta Jwt:Key.");

        // Mismos valores por defecto que Program.cs
        var issuer = configuration["Jwt:Issuer"] ?? "mascotitas";
        var audience = configuration["Jwt:Audience"] ?? "mascotitas";

        var horas = int.TryParse(configuration["Jwt:ExpireHours"], out var h) ? h : 8;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.Nombre),
                new Claim(ClaimTypes.Email, usuario.email),
                new Claim(ClaimTypes.Role,
                    usuario.Rol.Trim().Equals("admin", StringComparison.OrdinalIgnoreCase)
                        ? "Admin"
                        : usuario.Rol.Trim())
            }),
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(horas),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
