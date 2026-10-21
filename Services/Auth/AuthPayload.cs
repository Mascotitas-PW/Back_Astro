using back.Models;

namespace back.Services.Auth;

public class AuthPayload
{
    public string Token { get; set; } = string.Empty;
    public User Usuario { get; set; } = null!;

    public int Id {get; set; }
}
