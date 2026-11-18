using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace back.Services;

// Sube las imágenes a Cloudinary: viven fuera del servidor y no se borran en cada deploy.
// Devuelve la URL https completa; esa URL es la que se guarda en Producto.Imagen.
public class CloudinaryImageService : IImageService
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly string[] Extensiones = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly Cloudinary _cloudinary;

    // Se conserva por compatibilidad con Program.cs (imágenes antiguas guardadas en disco)
    public string RutaCarpeta { get; }

    public CloudinaryImageService(IWebHostEnvironment env, IConfiguration configuration)
    {
        RutaCarpeta = System.IO.Path.Combine(env.ContentRootPath, "uploads", "images");
        Directory.CreateDirectory(RutaCarpeta);

        var url = configuration["CLOUDINARY_URL"]
            ?? throw new InvalidOperationException("Falta CLOUDINARY_URL.");
        _cloudinary = new Cloudinary(url);
        _cloudinary.Api.Secure = true;
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

        // Verifica la firma real del archivo, no solo la extensión
        var cabecera = new byte[12];
        await using (var lectura = file.OpenReadStream())
        {
            await lectura.ReadExactlyAsync(cabecera, 0, (int)Math.Min(12, file.Length));
        }
        if (!EsImagenValida(cabecera))
            throw new ArgumentException("El archivo no es una imagen válida.");

        await using var stream = file.OpenReadStream();
        var parametros = new ImageUploadParams
        {
            File = new FileDescription($"{Guid.NewGuid():N}{ext}", stream),
            Folder = "mascotitas/productos",
            // Máx. 800 px, calidad y formato automáticos
            Transformation = new Transformation().Width(800).Height(800).Crop("limit").Quality("auto").FetchFormat("auto")
        };

        var resultado = await _cloudinary.UploadAsync(parametros);
        if (resultado.Error is not null || resultado.SecureUrl is null)
            throw new ArgumentException("No se pudo subir la imagen: " + (resultado.Error?.Message ?? "error desconocido"));

        return resultado.SecureUrl.ToString();
    }

    private static bool EsImagenValida(byte[] b) =>
        (b[0] == 0xFF && b[1] == 0xD8) ||
        (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||
        (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
         b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50);
}
