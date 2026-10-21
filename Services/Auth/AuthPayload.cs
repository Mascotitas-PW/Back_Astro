using back.Models;

namespace back.Services.Auth;

public class AuthPayload
{
    public string Token { get; set; } = string.Empty;

    // Datos del usuario directo en el payload (login { id nombre email rol token })
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;

    // También disponible anidado (login { token usuario { ... } })
    public User Usuario { get; set; } = null!;
}