namespace back.Models;

public class User
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    [GraphQLIgnore]
    public string Password { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}