using ECert.Data;
using ECert.Models;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class CertificateTemplateController : Controller
{
    private const long MaxImageSize = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private readonly ECertDbContext _db;
    private readonly CertificateTemplateService _templates;
    private readonly IWebHostEnvironment _env;
    private readonly AuditLogService _audit;

    public CertificateTemplateController(ECertDbContext db, CertificateTemplateService templates, IWebHostEnvironment env, AuditLogService audit)
    {
        _db = db;
        _templates = templates;
        _env = env;
        _audit = audit;
    }

    private bool IsSuperAdmin() => User.IsInRole("SuperAdmin");

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!IsSuperAdmin()) return Forbid();
        ViewData["Title"] = "إعدادات الشهادة";
        return View(await _templates.GetCurrentAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        CertificateTemplateSettings model,
        IFormFile? logo,
        IFormFile? rightSignature,
        IFormFile? leftSignature)
    {
        if (!IsSuperAdmin()) return Forbid();

        var current = await _db.CertificateTemplates.FirstOrDefaultAsync();
        var normalized = _templates.Normalize(model);
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "إعدادات الشهادة";
            normalized.CertificateTemplateSettingsId = model.CertificateTemplateSettingsId;
            normalized.LogoPath = current?.LogoPath;
            normalized.RightSignaturePath = current?.RightSignaturePath;
            normalized.LeftSignaturePath = current?.LeftSignaturePath;
            return View("Index", normalized);
        }

        var logoPath = await SaveImageAsync(logo, "logo");
        var rightSignaturePath = await SaveImageAsync(rightSignature, "right-signature");
        var leftSignaturePath = await SaveImageAsync(leftSignature, "left-signature");
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "إعدادات الشهادة";
            normalized.CertificateTemplateSettingsId = model.CertificateTemplateSettingsId;
            normalized.LogoPath = current?.LogoPath;
            normalized.RightSignaturePath = current?.RightSignaturePath;
            normalized.LeftSignaturePath = current?.LeftSignaturePath;
            return View("Index", normalized);
        }

        if (current == null)
        {
            normalized.LogoPath = logoPath;
            normalized.RightSignaturePath = rightSignaturePath;
            normalized.LeftSignaturePath = leftSignaturePath;
            normalized.TemplateVersion = 1;
            normalized.UpdatedAt = DateTime.Now;
            _db.CertificateTemplates.Add(normalized);
        }
        else
        {
            current.CertificateTitle = normalized.CertificateTitle;
            current.MainText = normalized.MainText;
            current.CenterName = normalized.CenterName;
            current.AdditionalText = normalized.AdditionalText;
            current.VerificationLabel = normalized.VerificationLabel;
            current.PrimaryColor = normalized.PrimaryColor;
            current.AccentColor = normalized.AccentColor;
            current.TextColor = normalized.TextColor;
            current.ShowVerificationCode = normalized.ShowVerificationCode;
            current.ShowCertificateNumber = normalized.ShowCertificateNumber;
            current.ShowIssueDate = normalized.ShowIssueDate;
            current.ShowVerificationUrl = normalized.ShowVerificationUrl;
            current.ElementOrder = normalized.ElementOrder;
            if (logoPath != null) current.LogoPath = logoPath;
            if (rightSignaturePath != null) current.RightSignaturePath = rightSignaturePath;
            if (leftSignaturePath != null) current.LeftSignaturePath = leftSignaturePath;
            current.TemplateVersion = Math.Max(1, current.TemplateVersion + 1);
            current.UpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        var version = current?.TemplateVersion ?? normalized.TemplateVersion;
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "CertificateTemplate", version, null, $"Saved template version {version}");
        TempData["Success"] = "تم حفظ إعدادات الشهادة. ستؤثر على الشهادات الجديدة فقط.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> SaveImageAsync(IFormFile? file, string kind)
    {
        if (file == null || file.Length == 0) return null;
        if (file.Length > MaxImageSize)
        {
            ModelState.AddModelError(string.Empty, "حجم كل صورة يجب ألا يتجاوز 2 ميغابايت.");
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            ModelState.AddModelError(string.Empty, "الصور المسموح بها هي PNG وJPG وJPEG وWEBP فقط.");
            return null;
        }

        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var read = await input.ReadAsync(header);
        if (!IsSupportedImage(header, read))
        {
            ModelState.AddModelError(string.Empty, "محتوى الملف ليس صورة آمنة من النوع المسموح.");
            return null;
        }

        var directory = Path.Combine(_env.WebRootPath, "uploads", "certificate-templates");
        Directory.CreateDirectory(directory);
        var fileName = $"{kind}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(directory, fileName);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.Position = 0;
        await input.CopyToAsync(output);
        return $"/uploads/certificate-templates/{fileName}";
    }

    private static bool IsSupportedImage(byte[] header, int length)
    {
        var png = length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        var jpeg = length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var webp = length >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;
        return png || jpeg || webp;
    }
}
