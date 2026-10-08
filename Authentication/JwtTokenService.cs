using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using back.Models;

public sealed class JwtTokenSettings(byte[] signingKey, string issuer, string audience)
{
    public byte[] SigningKey { get; } = signingKey;
    public string Issuer { get; } = issuer;
    public string Audience { get; } = audience;

    public static JwtTokenSettings Load(IConfiguration configuration)
    {
        var configuredKey = configuration["JWT_SIGNING_KEY"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException("JWT_SIGNING_KEY must be configured.");

        var signingKey = Encoding.UTF8.GetBytes(configuredKey);
        if (signingKey.Length < 32)
            throw new InvalidOperationException("JWT_SIGNING_KEY must contain at least 32 UTF-8 bytes.");

        return new JwtTokenSettings(
            signingKey,
            configuration["JWT_ISSUER"] ?? "Back_Astro",
            configuration["JWT_AUDIENCE"] ?? "Back_Astro_Frontend");
    }
}

public sealed class JwtTokenService(JwtTokenSettings settings)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public LoginResponse CreateLoginResponse(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(TokenLifetime);
        var userId = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("usuarioId", userId),
            new Claim(ClaimTypes.Name, user.Nombre),
            new Claim(ClaimTypes.Email, user.email),
            new Claim(ClaimTypes.Role, user.Rol)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(settings.SigningKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expiresAt,
            user.Id,
            user.Nombre,
            user.email,
            user.Rol);
    }
}

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    int Id,
    string Nombre,
    string email,
    string Rol);