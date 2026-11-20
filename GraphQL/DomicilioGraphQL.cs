using System.Security.Claims;
using back.Models;
using back.Services.Auth;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace back.GraphQL;

[GraphQLName("DomicilioInput")]
public record DomicilioInput(
    int? Id,
    string Alias,
    string NombreCompleto,
    string Telefono,
    string Direccion,
    string? CodigoPostal,
    string? Estado,
    string? Ciudad,
    string? Colonia,
    double? Latitud,
    double? Longitud,
    bool Predeterminado
);

[ExtendObjectType(typeof(Query))]
public class DomicilioQueries
{
    // Domicilios del usuario que inició sesión (el predeterminado primero)
    [Authorize]
    public async Task<List<Domicilio>> GetMisDomicilios(
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var usuarioId = AuthHelper.ObtenerUsuarioId(user);
        return await context.Domicilios
            .AsNoTracking()
            .Where(d => d.UsuarioId == usuarioId)
            .OrderByDescending(d => d.Predeterminado)
            .ThenByDescending(d => d.Id)
            .ToListAsync();
    }

    // ¿Ya aceptó esta versión de los términos y el aviso de privacidad?
    [Authorize]
    public async Task<bool> GetTerminosAceptados(
        string version,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var usuarioId = AuthHelper.ObtenerUsuarioId(user);
        return await context.AceptacionesLegales
            .AnyAsync(a => a.UsuarioId == usuarioId && a.Version == version);
    }
}

[ExtendObjectType(typeof(Mutation))]
public class DomicilioMutation
{
    private const int MaxDomicilios = 10;

    [Authorize]
    public async Task<Domicilio> GuardarDomicilio(
        DomicilioInput input,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var usuarioId = AuthHelper.ObtenerUsuarioId(user);

        var alias = (input.Alias ?? "").Trim();
        var nombre = (input.NombreCompleto ?? "").Trim();
        var direccion = (input.Direccion ?? "").Trim();
        var telefono = new string((input.Telefono ?? "").Where(char.IsDigit).ToArray());
        var cp = new string((input.CodigoPostal ?? "").Where(char.IsDigit).ToArray());
        var estado = (input.Estado ?? "").Trim();
        var ciudad = (input.Ciudad ?? "").Trim();
        var colonia = (input.Colonia ?? "").Trim();

        if (alias.Length is < 1 or > 40) throw new GraphQLException("Escribe un nombre para el domicilio (máx. 40 caracteres).");
        if (nombre.Length < 3) throw new GraphQLException("Escribe el nombre completo.");
        if (direccion.Length < 5) throw new GraphQLException("Escribe una dirección válida.");
        if (telefono.Length != 10) throw new GraphQLException("El teléfono debe tener 10 dígitos.");
        if (cp.Length != 0 && cp.Length != 5) throw new GraphQLException("El código postal debe tener 5 dígitos.");

        double? lat = input.Latitud is >= -90 and <= 90 ? input.Latitud : null;
        double? lng = input.Longitud is >= -180 and <= 180 ? input.Longitud : null;
        if (lat is null || lng is null) { lat = null; lng = null; }

        Domicilio domicilio;
        if (input.Id is int id)
        {
            domicilio = await context.Domicilios.FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId)
                ?? throw new GraphQLException("El domicilio no existe.");
        }
        else
        {
            if (await context.Domicilios.CountAsync(d => d.UsuarioId == usuarioId) >= MaxDomicilios)
                throw new GraphQLException($"Puedes guardar hasta {MaxDomicilios} domicilios.");
            domicilio = new Domicilio { UsuarioId = usuarioId };
            context.Domicilios.Add(domicilio);
        }

        domicilio.Alias = alias;
        domicilio.NombreCompleto = nombre;
        domicilio.Telefono = telefono;
        domicilio.Direccion = direccion;
        domicilio.CodigoPostal = cp.Length == 0 ? null : cp;
        domicilio.Estado = estado.Length == 0 ? null : estado;
        domicilio.Ciudad = ciudad.Length == 0 ? null : ciudad;
        domicilio.Colonia = colonia.Length == 0 ? null : colonia;
        domicilio.Latitud = lat;
        domicilio.Longitud = lng;

        var hayOtros = await context.Domicilios.AnyAsync(d => d.UsuarioId == usuarioId && d.Id != domicilio.Id);
        domicilio.Predeterminado = input.Predeterminado || !hayOtros; // el primero siempre es predeterminado

        if (domicilio.Predeterminado)
        {
            var otros = await context.Domicilios
                .Where(d => d.UsuarioId == usuarioId && d.Id != domicilio.Id && d.Predeterminado)
                .ToListAsync();
            foreach (var o in otros) o.Predeterminado = false;
        }

        await context.SaveChangesAsync();
        return domicilio;
    }

    [Authorize]
    public async Task<bool> EliminarDomicilio(
        int id,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var usuarioId = AuthHelper.ObtenerUsuarioId(user);
        var domicilio = await context.Domicilios.FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId)
            ?? throw new GraphQLException("El domicilio no existe.");

        context.Domicilios.Remove(domicilio);
        await context.SaveChangesAsync();
        return true;
    }

    // Guarda la constancia de aceptación (usuario, versión y fecha)
    [Authorize]
    public async Task<bool> AceptarTerminos(
        string version,
        ClaimsPrincipal user,
        [Service] AppDbContext context)
    {
        var usuarioId = AuthHelper.ObtenerUsuarioId(user);
        version = (version ?? "").Trim();
        if (version.Length is < 1 or > 20) throw new GraphQLException("Versión no válida.");

        if (!await context.AceptacionesLegales.AnyAsync(a => a.UsuarioId == usuarioId && a.Version == version))
        {
            context.AceptacionesLegales.Add(new AceptacionLegal
            {
                UsuarioId = usuarioId,
                Version = version,
                Fecha = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }
        return true;
    }
}
