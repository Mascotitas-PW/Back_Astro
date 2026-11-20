namespace back.Models
{
    public class EstadisticasDto
    {
        public decimal TotalVentas { get; set; }
        public int TotalPedidos { get; set; }
        public decimal TicketPromedio { get; set; }
        public List<VentaDiaDto> VentasPorDia { get; set; } = new();
        public List<TopProductoDto> TopProductos { get; set; } = new();
        public List<VentaCategoriaDto> VentasPorCategoria { get; set; } = new();
    }

    public class VentaDiaDto
    {
        public string Fecha { get; set; } = string.Empty; // yyyy-MM-dd
        public decimal Total { get; set; }
        public int Pedidos { get; set; }
    }

    public class TopProductoDto
    {
        public int ProductoId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int Unidades { get; set; }
        public decimal Ingresos { get; set; }
    }

    public class VentaCategoriaDto
    {
        public string Categoria { get; set; } = string.Empty;
        public decimal Ingresos { get; set; }
    }
}
