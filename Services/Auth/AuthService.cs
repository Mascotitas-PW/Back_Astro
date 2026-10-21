using back.Models;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace back.Services.Auth;

public class AuthService(AppDbContext context, IJwtService jwt) : IAuthService
{
    public async Task<string> RegistrarAsync(string nombre, string email, string password)
    {
        nombre = (nombre ?? "").Trim();
        email = (email ?? "").Trim().ToLowerInvariant();

        if (nombre.Length < 2)
            throw new GraphQLException("Escribe tu nombre.");

        if (!email.Contains('@') || email.Length < 5)
            throw new GraphQLException("El correo no es válido.");

        if ((password ?? "").Length < 6)
            throw new GraphQLException("La contraseña debe tener al menos 6 caracteres.");

        if (await context.Usuarios.AnyAsync(u => u.email == email))
            throw new GraphQLException("Ya existe una cuenta con ese correo.");

        context.Usuarios.Add(new User
        {
            Nombre = nombre,
            email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            // Siempre Cliente: el rol enviado por el cliente se ignora.
            Rol = "Cliente"
        });

        await context.SaveChangesAsync();
        return "Usuario registrado correctamente.";
    }

    public async Task<AuthPayload> LoginAsync(string email, string password)
    {
        email = (email ?? "").Trim().ToLowerInvariant();

        var usuario = await context.Usuarios.FirstOrDefaultAsync(u => u.email == email);

        if (usuario is null || !VerificarPassword(usuario, password ?? ""))
            throw new GraphQLException("Correo o contraseña incorrectos.");

        // Si el usuario se guardó antes con la contraseña en texto plano,
        // se convierte a hash en su primer login correcto.
        if (!usuario.Password.StartsWith("$2"))
        {
            usuario.Password = BCrypt.Net.BCrypt.HashPassword(password);
            await context.SaveChangesAsync();
        }

        return new AuthPayload
        {
            Token = jwt.GenerateToken(usuario),
            Id = usuario.Id,
            Nombre = usuario.Nombre,
            Email = usuario.email,
            Rol = usuario.Rol,
            Usuario = usuario
        };
    }

    private static bool VerificarPassword(User usuario, string password)
    {
        if (string.IsNullOrEmpty(usuario.Password)) return false;

        return usuario.Password.StartsWith("$2")
            ? BCrypt.Net.BCrypt.Verify(password, usuario.Password)
            : usuario.Password == password; // legado: texto plano
    }
}