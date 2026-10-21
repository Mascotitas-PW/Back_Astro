namespace back.Models
{
    public class Pedido
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public float Total { get; set; }
    public string Status { get; set; } = "Pendiente";
    public int UsuarioId { get; set; }
    public string? NombreCompleto   { get; set; }
    public string? DireccionEntrega { get; set; }
    public string? Telefono         { get; set; }
}
}
