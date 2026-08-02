using ECert.Data;
using ECert.Models;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class CertificatesController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly NotificationService _notify;
    public CertificatesController(ECertDbContext db, AuditLogService audit, NotificationService notify)
    { _db = db; _audit = audit; _notify = notify; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public async Task<IActionResult> Index(string? search)
    {
        if (!HasPermission("issue-certificates")) return Forbid();
        var query = _db.Certificates.Include(c => c.Registration).ThenInclude(r => r!.Course).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.TraineeName.Contains(search) || c.CertificateNumber.Contains(search));
        ViewBag.Search = search;
        var certificates = await query.OrderByDescending(c => c.IssueDate).ToListAsync();
        return View(certificates);
    }

    [HttpGet]
    public async Task<IActionResult> Issue()
    {
        if (!HasPermission("issue-certificates")) return Forbid();
        // Show accepted registrations in completed courses that don't have certificates yet
        var eligible = await _db.Registrations
            .Include(r => r.Course)
            .Where(r => r.Status == "Accepted" && !r.CertificateIssued && r.Course!.Status == "Completed")
            .ToListAsync();
        ViewBag.Eligible = eligible;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(int registrationId)
    {
        if (!HasPermission("issue-certificates")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == registrationId);
        if (reg == null) return NotFound();

        // Prevent duplicate
        if (reg.CertificateIssued)
        {
            TempData["Error"] = "تم إصدار شهادة لهذا المتدرب مسبقاً في نفس الدورة.";
            return RedirectToAction("Issue");
        }

        var certNumber = $"CERT-{DateTime.Now:yyyy}-{new Random().Next(1000, 9999)}";
        var verificationCode = Guid.NewGuid().ToString("N")[..12].ToUpper();

        var certificate = new Certificate
        {
            CertificateNumber = certNumber,
            RegistrationId = registrationId,
            TraineeName = reg.FullName,
            CourseName = reg.Course?.CourseName ?? "",
            IssueDate = DateTime.Now,
            IssuedBy = User.Identity?.Name ?? "System",
            VerificationCode = verificationCode,
            CreatedAt = DateTime.Now
        };

        _db.Certificates.Add(certificate);
        reg.CertificateIssued = true;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Issue", "Certificate", certificate.CertificateId, null, $"Cert: {certNumber}, Trainee: {reg.FullName}");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "CertificateIssued",
            $"عزيزي {reg.FullName}، تم إصدار شهادتك في دورة {reg.Course?.CourseName}. رقم الشهادة: {certNumber}", "SMS", "Certificate", certificate.CertificateId);

        TempData["Success"] = $"تم إصدار الشهادة {certNumber} بنجاح.";
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> View(int id)
    {
        var cert = await _db.Certificates.Include(c => c.Registration).ThenInclude(r => r!.Course).FirstOrDefaultAsync(c => c.CertificateId == id);
        if (cert == null) return NotFound();

        // Generate QR Code
        using var qrGenerator = new QRCoder.QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(cert.CertificateNumber + "|" + cert.VerificationCode, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qrCode = new QRCoder.PngByteQRCode(qrData);
        var qrBytes = qrCode.GetGraphic(5);
        ViewBag.QrCodeBase64 = Convert.ToBase64String(qrBytes);

        return View(cert);
    }

    // Public verification
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Verify() => View();

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["Error"] = "الرجاء إدخال رمز التحقق.";
            return RedirectToAction("Verify");
        }
        var cert = await _db.Certificates.Include(c => c.Registration).ThenInclude(r => r!.Course)
            .FirstOrDefaultAsync(c => c.VerificationCode == code.Trim().ToUpper() || c.CertificateNumber == code.Trim());
        ViewBag.Certificate = cert;
        ViewBag.SearchCode = code;
        return View("VerifyResult");
    }
}
