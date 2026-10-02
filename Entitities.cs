namespace back.Models;

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class Producto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string Imagen { get; set; } = string.Empty;
    public int Stock { get; set; }
    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
}


public class Pedido
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public float Total { get; set; }
    public string Status { get; set; } = "Pendiente";
    public int UsuarioId { get; set; }
}


public class DetallePedido
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ProductoId { get; set; }
    public int Cantidad { get; set; }
    public float PrecioUnitario { get; set; }
}