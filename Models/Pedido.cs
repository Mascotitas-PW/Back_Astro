namespace back.Models
{
    public class Pedido
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public float Total { get; set; }
        public string Status { get; set; } = "Pendiente";
        public int UsuarioId { get; set; }
        public int NumeroPedidoUsuario { get; set; }
        public string? NombreCompleto { get; set; }
        public string? DireccionEntrega { get; set; }
        public string? CodigoPostalEntrega { get; set; }
        public string? EstadoEntrega { get; set; }
        public string? CiudadEntrega { get; set; }
        public string? ColoniaEntrega { get; set; }
        public string? Telefono { get; set; }
        public double? Latitud { get; set; }
        public double? Longitud { get; set; }
        public Guid? CotizacionEnvioId { get; set; }
        public string? SkydropxRateId { get; set; }
        public string? Paqueteria { get; set; }
        public string? ServicioEnvio { get; set; }
        public decimal? CostoEnvio { get; set; }
    }
}
