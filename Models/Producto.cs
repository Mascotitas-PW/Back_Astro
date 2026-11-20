namespace back.Models
{
    public class Producto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string Imagen { get; set; } = string.Empty;
        public int Stock { get; set; }
<<<<<<< HEAD
=======
        public decimal? PesoKg { get; set; }
>>>>>>> 95a2c0e (envios)
        public int CategoriaId { get; set; }
        public Categoria? Categoria { get; set; }
    }
}
