namespace back.Services;

public class ImageService : IImageService
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly string[] Extensiones = { ".jpg", ".jpeg", ".png", ".webp" };

    public string RutaCarpeta { get; }

    public ImageService(IWebHostEnvironment env)
    {
        RutaCarpeta = System.IO.Path.Combine(env.ContentRootPath, "uploads", "images");
        Directory.CreateDirectory(RutaCarpeta); // PhysicalFileProvider exige que exista
    }

    public async Task<string> GuardarAsync(IFormFile file)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("No se envió ninguna imagen.");

        if (file.Length > MaxBytes)
            throw new ArgumentException("La imagen no puede pesar más de 5 MB.");

        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Extensiones.Contains(ext))
            throw new ArgumentException("Formato no permitido. Usa JPG, PNG o WEBP.");

        // Verificar la firma real del archivo, no solo la extensión.
        var cabecera = new byte[12];
        await using (var lectura = file.OpenReadStream())
        {
            await lectura.ReadExactlyAsync(cabecera, 0, (int)Math.Min(12, file.Length));
        }

        if (!EsImagenValida(cabecera))
            throw new ArgumentException("El archivo no es una imagen válida.");

        var nombre = $"{Guid.NewGuid():N}{ext}";
        await using var destino = File.Create(System.IO.Path.Combine(RutaCarpeta, nombre));
        await file.CopyToAsync(destino);

        return nombre;
    }

    private static bool EsImagenValida(byte[] b) =>
        (b[0] == 0xFF && b[1] == 0xD8) ||                                   // JPEG
        (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||   // PNG
        (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
         b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50);   // WEBP
}