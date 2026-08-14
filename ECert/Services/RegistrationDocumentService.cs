using Microsoft.AspNetCore.Http;

namespace ECert.Services;

public sealed record SavedRegistrationDocument(string RelativePath, string OriginalName);

public class RegistrationDocumentService
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp"
    };

    private readonly IWebHostEnvironment _env;

    public RegistrationDocumentService(IWebHostEnvironment env) => _env = env;

    public async Task<SavedRegistrationDocument> SaveAsync(IFormFile? file)
    {
        if (file == null) throw new InvalidDataException("المستند مطلوب.");
        if (file.Length <= 0) throw new InvalidDataException("المستند فارغ.");
        if (file.Length > MaxFileSize) throw new InvalidDataException("حجم المستند يجب ألا يتجاوز 10 ميغابايت.");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidDataException("المستند المسموح به هو PDF أو PNG أو JPG أو JPEG أو WEBP فقط.");

        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var read = await input.ReadAsync(header);
        if (!IsAllowedContent(header, read, extension))
            throw new InvalidDataException("محتوى الملف لا يطابق نوع المستند المسموح.");

        var directory = Path.Combine(_env.ContentRootPath, "uploads", "registrations");
        Directory.CreateDirectory(directory);
        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(directory, storedName);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.Position = 0;
        await input.CopyToAsync(output);

        var originalName = SanitizeOriginalName(file.FileName);
        return new SavedRegistrationDocument($"uploads/registrations/{storedName}", originalName);
    }

    public string ResolveFullPath(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "uploads", "registrations"));
        var combined = Path.GetFullPath(Path.Combine(_env.ContentRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("مسار المستند غير صالح.");
        return combined;
    }

    private static string SanitizeOriginalName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        var chars = safe.Where(c => !char.IsControl(c)).ToArray();
        safe = new string(chars).Trim();
        return safe.Length <= 255 ? safe : safe[..255];
    }

    private static bool IsAllowedContent(byte[] header, int length, string extension)
    {
        var png = length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        var jpeg = length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var webp = length >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;
        var pdf = length >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D;

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => pdf,
            ".png" => png,
            ".jpg" or ".jpeg" => jpeg,
            ".webp" => webp,
            _ => false
        };
    }
}
