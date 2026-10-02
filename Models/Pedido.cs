ublic class Pedido
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public float Total { get; set; }
    public string Status { get; set; } = "Pendiente";
    public int UsuarioId { get; set; }
}