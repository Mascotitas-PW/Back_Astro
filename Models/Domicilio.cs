namespace back.Models;

// Domicilio guardado de un usuario (se reutiliza en el checkout)
public class Domicilio
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Alias { get; set; } = "Casa";
    public string NombreCompleto { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string? CodigoPostal { get; set; }
    public string? Estado { get; set; }
    public string? Ciudad { get; set; }
    public string? Colonia { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public bool Predeterminado { get; set; }
}

// Constancia de que el usuario aceptó términos y aviso de privacidad (versión y fecha)
public class AceptacionLegal
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
}
