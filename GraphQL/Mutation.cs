using Microsoft.EntityFrameworkCore;
using back.Models; // O el namespace donde esté tu modelo User
using back.GraphQL; // <-- Agrega esta línea arriba de todo en Program.cs
using HotChocolate; // <-- Asegúrate de incluir este using
using back.Models;

namespace back.GraphQL;

public class Mutation
{
    public async Task<string> Registrar(
        string nombre, 
        string email, 
        string password, 
        [Service] AppDbContext context)
    {
        var emailLimpio = email.Trim().ToLower();

        var existe = await context.Usuarios.AnyAsync(u => u.email.ToLower() == emailLimpio);
        if (existe)
        {
            // Crea un error explícito de GraphQL que Hot Chocolate SÍ enviará al frontend
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("El correo electrónico ya está registrado.")
                    .SetCode("EMAIL_DUPLICADO")
                    .Build()
            );
        }

        var nuevoUsuario = new User 
        { 
            Nombre = nombre.Trim(), 
            email = emailLimpio, 
            Password = password, 
            Rol = "Cliente" 
        };

        context.Usuarios.Add(nuevoUsuario);
        await context.SaveChangesAsync();

        return "Usuario registrado con éxito";
    }

     public async Task<User?> Login(
        string email,
        string password,
        [Service] AppDbContext context)
    {
        return await context.Usuarios
            .FirstOrDefaultAsync(u =>
                u. email == email &&
                u.Password == password
            );
    }
    
}