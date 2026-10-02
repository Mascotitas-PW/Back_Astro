namespace back.Models{
public class DetallePedido
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ProductoId { get; set; }
    public int Cantidad { get; set; }
    public float PrecioUnitario { get; set; }
}
}
