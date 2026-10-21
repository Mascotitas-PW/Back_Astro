namespace back.Services;

public interface IImageService
{
    
    string RutaCarpeta { get; }

    Task<string> GuardarAsync(IFormFile file);
}
