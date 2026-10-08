using System.Security.Claims;
using System.Text;
using back.Models;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Back.Tests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void LoginTokenValidatesAndContainsAuthenticatedUserIdentity()
    {
        var settings = new JwtTokenSettings(
            Encoding.UTF8.GetBytes("test-only-signing-key-with-at-least-32-bytes"),
            "test-issuer",
            "test-audience");
        var service = new JwtTokenService(settings);
        var response = service.CreateLoginResponse(new User
        {
            Id = 42,
            Nombre = "Test User",
            email = "test@example.test",
            Rol = "Cliente",
            Password = "password-hash-must-not-be-returned"
        });

        var principal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(
            response.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKey),
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            },
            out _);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(42, response.Id);
        Assert.Equal("test@example.test", response.email);
        Assert.DoesNotContain("password", response.AccessToken, StringComparison.OrdinalIgnoreCase);
        Assert.True(PayPalCheckoutEndpoints.TryGetAuthenticatedUserId(principal, out var userId));
        Assert.Equal(42, userId);
        Assert.Contains(principal.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Cliente");
    }

    [Fact]
    public void SigningKeyMustBeAtLeast32Bytes()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationManager
        {
            ["JWT_SIGNING_KEY"] = "too-short-key"
        };

        Assert.Throws<InvalidOperationException>(() => { _ = JwtTokenSettings.Load(configuration); });
    }
}