namespace back.Models;

public sealed class CotizacionEnvio
{
    public Guid Id { get; set; }
    public int UsuarioId { get; set; }
    public string SkydropxId { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Ciudad { get; set; } = string.Empty;
    public string Colonia { get; set; } = string.Empty;
    public string TarifasJson { get; set; } = "[]";
    public DateTime CreadaUtc { get; set; }
    public DateTime VenceUtc { get; set; }
}

public sealed record TarifaEnvio(
    string Id,
    string Paqueteria,
    string Servicio,
    decimal Importe,
    string Moneda,
    int? DiasEntrega);