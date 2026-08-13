using System.Net.Http.Json;
using ClosedXML.Excel;
using ECert.Data;
using ECert.Models;
using ECert.Models.ViewModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class CertificatesController : Controller
{
    private const string InvalidVerifyMessage = "رقم الشهادة غير صحيح أو غير موجود.";
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly NotificationService _notify;
    private readonly CertificateSecurityService _certificateSecurity;
    private readonly VerifyRequestGuardService _verifyGuard;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public CertificatesController(
        ECertDbContext db,
        AuditLogService audit,
        NotificationService notify,
        CertificateSecurityService certificateSecurity,
        VerifyRequestGuardService verifyGuard,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
        _certificateSecurity = certificateSecurity;
        _verifyGuard = verifyGuard;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int? courseId, DateTime? issueFrom, DateTime? issueTo)
    {
        if (!HasPermission("issue-certificates")) return Forbid();

        var query = _db.Certificates
            .Include(c => c.Registration)
            .ThenInclude(r => r!.Course)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmedSearch = search.Trim();
            query = query.Where(c =>
                c.TraineeName.Contains(trimmedSearch) ||
                c.CourseName.Contains(trimmedSearch) ||
                c.CertificateNumber.Contains(trimmedSearch) ||
                c.PublicId.Contains(trimmedSearch));
        }

        if (courseId.HasValue)
            query = query.Where(c => c.Registration != null && c.Registration.CourseId == courseId.Value);

        if (issueFrom.HasValue)
        {
            var start = issueFrom.Value.Date;
            query = query.Where(c => c.IssueDate >= start);
        }

        if (issueTo.HasValue)
        {
            var end = issueTo.Value.Date.AddDays(1);
            query = query.Where(c => c.IssueDate < end);
        }

        var certificates = await query
            .OrderByDescending(c => c.IssueDate)
            .ThenByDescending(c => c.CertificateId)
            .ToListAsync();

        var model = new CertificateIndexPageViewModel
        {
            Search = search,
            CourseId = courseId,
            IssueFrom = issueFrom,
            IssueTo = issueTo,
            Courses = await _db.Courses.OrderBy(c => c.CourseName).ToListAsync(),
            Certificates = certificates,
            VerificationUrls = certificates.ToDictionary(
                c => c.CertificateId,
                c => BuildVerificationUrl(c))
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Issue(int? courseId, DateTime? registrationFrom, DateTime? registrationTo)
    {
        if (!HasPermission("issue-certificates")) return Forbid();

        var query = _db.Registrations
            .Include(r => r.Course)
            .Where(r => r.Status == "Accepted" && !r.CertificateIssued && r.Course != null && r.Course.Status == "Completed")
            .AsQueryable();

        if (courseId.HasValue)
            query = query.Where(r => r.CourseId == courseId.Value);

        if (registrationFrom.HasValue)
        {
            var start = registrationFrom.Value.Date;
            query = query.Where(r => r.RegistrationDate >= start);
        }

        if (registrationTo.HasValue)
        {
            var end = registrationTo.Value.Date.AddDays(1);
            query = query.Where(r => r.RegistrationDate < end);
        }

        var model = new CertificateIssuePageViewModel
        {
            CourseId = courseId,
            RegistrationFrom = registrationFrom,
            RegistrationTo = registrationTo,
            Courses = await _db.Courses.Where(c => c.Status == "Completed").OrderBy(c => c.CourseName).ToListAsync(),
            EligibleRegistrations = await query.OrderByDescending(r => r.RegistrationDate).ThenBy(r => r.FullName).ToListAsync()
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult IssueBulk()
    {
        if (!HasPermission("issue-certificates")) return Forbid();

        TempData["Error"] = "افتح صفحة إصدار الشهادات وحدد المتدربين قبل تنفيذ الإصدار.";
        return RedirectToAction(nameof(Issue));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueBulk(int[] registrationIds, string? certificatePrefix)
    {
        if (!HasPermission("issue-certificates")) return Forbid();

        var ids = registrationIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<int>();
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            TempData["Error"] = "الرجاء تحديد متدرب واحد على الأقل لإصدار الشهادات.";
            return RedirectToAction(nameof(Issue));
        }

        string normalizedCertificatePrefix;
        try
        {
            normalizedCertificatePrefix = _certificateSecurity.NormalizeCertificatePrefix(certificatePrefix);
        }
        catch (ArgumentException exception)
        {
            TempData["Error"] = exception.Message;
            return RedirectToAction(nameof(Issue));
        }

        var registrations = await _db.Registrations
            .Include(r => r.Course)
            .Where(r => idList.Contains(r.RegistrationId) && r.Status == "Accepted" && !r.CertificateIssued && r.Course != null && r.Course.Status == "Completed")
            .OrderBy(r => r.RegistrationId)
            .ToListAsync();

        if (!registrations.Any())
        {
            TempData["Error"] = "العناصر المحددة غير مؤهلة لإصدار الشهادات أو تم إصدارها مسبقاً.";
            return RedirectToAction(nameof(Issue));
        }

        var issuedCertificates = new List<Certificate>();
        var reservedCertificateNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedPublicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedVerificationCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var transaction = await _db.Database.BeginTransactionAsync();

        foreach (var registration in registrations)
        {
            var certificate = new Certificate
            {
                CertificateNumber = await GenerateUniqueCertificateNumberAsync(normalizedCertificatePrefix, reservedCertificateNumbers),
                PublicId = await GenerateUniquePublicIdAsync(reservedPublicIds),
                VerificationCode = await GenerateUniqueVerificationCodeAsync(reservedVerificationCodes),
                RegistrationId = registration.RegistrationId,
                TraineeName = registration.FullName,
                CourseName = registration.Course?.CourseName ?? string.Empty,
                IssueDate = DateTime.Now,
                IssuedBy = User.Identity?.Name ?? "System",
                Status = "Valid",
                SignatureVersion = 1,
                CreatedAt = DateTime.Now
            };

            issuedCertificates.Add(certificate);
            _db.Certificates.Add(certificate);
            registration.CertificateIssued = true;
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var issuedBy = User.Identity?.Name ?? "System";
        var certNumbers = string.Join(", ", issuedCertificates.Select(c => c.CertificateNumber));
        await _audit.LogAsync(issuedBy, "IssueBulk", "Certificate", null, null, $"Prefix: {normalizedCertificatePrefix}; Count: {issuedCertificates.Count}; Certificates: {certNumbers}", HttpContext.Connection.RemoteIpAddress?.ToString());

        foreach (var registration in registrations)
        {
            var certificate = issuedCertificates.First(c => c.RegistrationId == registration.RegistrationId);
            await _notify.SendAsync(
                registration.FullName,
                registration.Phone,
                registration.Email,
                "CertificateIssued",
                $"عزيزي {registration.FullName}، تم إصدار شهادتك في دورة {registration.Course?.CourseName}. رقم الشهادة: {certificate.CertificateNumber}",
                "SMS",
                "Certificate",
                certificate.CertificateId);
        }

        TempData["Success"] = $"تم إصدار {issuedCertificates.Count} شهادة بنجاح بالبادئة {normalizedCertificatePrefix}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportSelected(int[] certificateIds)
    {
        if (!HasPermission("issue-certificates")) return Forbid();

        var ids = certificateIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<int>();
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            TempData["Error"] = "الرجاء تحديد شهادة واحدة على الأقل للتصدير.";
            return RedirectToAction(nameof(Index));
        }

        var certificates = await _db.Certificates
            .Include(c => c.Registration)
            .ThenInclude(r => r!.Course)
            .Where(c => idList.Contains(c.CertificateId))
            .OrderByDescending(c => c.IssueDate)
            .ToListAsync();

        if (!certificates.Any())
        {
            TempData["Error"] = "لم يتم العثور على شهادات مطابقة للتصدير.";
            return RedirectToAction(nameof(Index));
        }

        return BuildExcelExport(certificates, "selected");
    }

    [HttpGet]
    public async Task<IActionResult> ExportFiltered(string? search, int? courseId, DateTime? issueFrom, DateTime? issueTo)
    {
        if (!HasPermission("issue-certificates")) return Forbid();

        var query = _db.Certificates
            .Include(c => c.Registration)
            .ThenInclude(r => r!.Course)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmedSearch = search.Trim();
            query = query.Where(c =>
                c.TraineeName.Contains(trimmedSearch) ||
                c.CourseName.Contains(trimmedSearch) ||
                c.CertificateNumber.Contains(trimmedSearch) ||
                c.PublicId.Contains(trimmedSearch));
        }

        if (courseId.HasValue)
            query = query.Where(c => c.Registration != null && c.Registration.CourseId == courseId.Value);

        if (issueFrom.HasValue)
        {
            var start = issueFrom.Value.Date;
            query = query.Where(c => c.IssueDate >= start);
        }

        if (issueTo.HasValue)
        {
            var end = issueTo.Value.Date.AddDays(1);
            query = query.Where(c => c.IssueDate < end);
        }

        var certificates = await query
            .OrderByDescending(c => c.IssueDate)
            .ThenByDescending(c => c.CertificateId)
            .ToListAsync();

        if (!certificates.Any())
        {
            TempData["Error"] = "لا توجد شهادات مطابقة للفلاتر الحالية للتصدير.";
            return RedirectToAction(nameof(Index), new { search, courseId, issueFrom, issueTo });
        }

        return BuildExcelExport(certificates, "filtered");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Verify()
    {
        ViewBag.TurnstileSiteKey = GetTurnstileSiteKey();
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string code)
    {
        if (!IsVerifyRequestAllowed())
            return BuildRateLimitedVerifyResult(code);

        var turnstileResponse = Request.Form["cf-turnstile-response"].FirstOrDefault();
        if (!await IsTurnstileValidAsync(turnstileResponse))
            return BuildInvalidVerifyResult(code);

        var normalizedCode = NormalizeLookupCode(code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
            return BuildInvalidVerifyResult(code);

        var certificate = await _db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CertificateNumber == normalizedCode || c.VerificationCode == normalizedCode);

        return BuildVerifyResult(certificate, code);
    }

    [AllowAnonymous]
    [HttpGet("/v/{publicId}")]
    public async Task<IActionResult> PublicVerify(string publicId, string? sig)
    {
        if (!IsVerifyRequestAllowed())
            return BuildRateLimitedVerifyResult(publicId);

        if (string.IsNullOrWhiteSpace(publicId) || !_certificateSecurity.VerifySignature(publicId, sig))
            return BuildInvalidVerifyResult(publicId);

        var normalizedPublicId = publicId.Trim();
        var certificate = await _db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.PublicId == normalizedPublicId);

        return BuildVerifyResult(certificate, normalizedPublicId);
    }

    private FileResult BuildExcelExport(List<Certificate> certificates, string scope)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Certificates");
        worksheet.RightToLeft = true;

        var headers = new[]
        {
            "رقم الشهادة",
            "اسم الطالب",
            "الدورة",
            "تاريخ الإصدار",
            "الحالة",
            "المعرف العام",
            "رمز التحقق",
            "رابط QR"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFF");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (var row = 0; row < certificates.Count; row++)
        {
            var certificate = certificates[row];
            var excelRow = row + 2;

            worksheet.Cell(excelRow, 1).Value = certificate.CertificateNumber;
            worksheet.Cell(excelRow, 2).Value = certificate.TraineeName;
            worksheet.Cell(excelRow, 3).Value = certificate.CourseName;
            worksheet.Cell(excelRow, 4).Value = certificate.IssueDate.ToString("yyyy-MM-dd");
            worksheet.Cell(excelRow, 5).Value = MapCertificateStatus(certificate.Status);
            worksheet.Cell(excelRow, 6).Value = certificate.PublicId;
            worksheet.Cell(excelRow, 7).Value = certificate.VerificationCode;
            worksheet.Cell(excelRow, 8).Value = BuildVerificationUrl(certificate);
        }

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"certificates_{scope}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private IActionResult BuildVerifyResult(Certificate? certificate, string? searchCode)
    {
        var model = new PublicCertificateVerificationViewModel
        {
            SearchCode = searchCode?.Trim() ?? string.Empty
        };

        if (certificate == null)
        {
            model.IsValid = false;
            model.Message = InvalidVerifyMessage;
            return View("VerifyResult", model);
        }

        model.CertificateFound = true;
        model.IsValid = string.Equals(certificate.Status, "Valid", StringComparison.OrdinalIgnoreCase);
        model.Message = model.IsValid
            ? "تم العثور على شهادة سارية."
            : string.Equals(certificate.Status, "Revoked", StringComparison.OrdinalIgnoreCase)
                ? "تم العثور على الشهادة، لكنها ملغاة ولا يمكن اعتمادها."
                : "تم العثور على الشهادة، لكن حالتها غير معروفة. يرجى مراجعة الجهة المصدرة.";
        model.CertificateNumber = certificate.CertificateNumber;
        model.TraineeName = certificate.TraineeName;
        model.CourseName = certificate.CourseName;
        model.IssueDate = certificate.IssueDate;
        model.Status = MapCertificateStatus(certificate.Status);
        return View("VerifyResult", model);
    }

    private IActionResult BuildInvalidVerifyResult(string? searchCode)
    {
        var model = new PublicCertificateVerificationViewModel
        {
            IsValid = false,
            SearchCode = searchCode?.Trim() ?? string.Empty,
            Message = InvalidVerifyMessage
        };

        return View("VerifyResult", model);
    }

    private IActionResult BuildRateLimitedVerifyResult(string? searchCode)
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var model = new PublicCertificateVerificationViewModel
        {
            IsValid = false,
            SearchCode = searchCode?.Trim() ?? string.Empty,
            Message = "تم تجاوز عدد المحاولات المسموح بها. حاول مرة أخرى لاحقاً."
        };

        return View("VerifyResult", model);
    }

    private string BuildVerificationUrl(Certificate certificate)
        => _certificateSecurity.BuildVerificationUrl(certificate.PublicId, GetPublicBaseUrl());

    private string GetPublicBaseUrl()
    {
        var configured = _configuration["PUBLIC_BASE_URL"] ?? _configuration["CertificateSecurity:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        return $"{Request.Scheme}://{Request.Host}";
    }

    private bool IsVerifyRequestAllowed() => _verifyGuard.IsAllowed(HttpContext);

    private string? GetTurnstileSiteKey()
        => _configuration["TURNSTILE_SITE_KEY"] ?? _configuration["CertificateSecurity:TurnstileSiteKey"];

    private async Task<bool> IsTurnstileValidAsync(string? token)
    {
        var secretKey = _configuration["TURNSTILE_SECRET_KEY"] ?? _configuration["CertificateSecurity:TurnstileSecretKey"];
        var siteKey = GetTurnstileSiteKey();

        if (string.IsNullOrWhiteSpace(siteKey) || string.IsNullOrWhiteSpace(secretKey))
            return true;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            using var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = secretKey,
                    ["response"] = token,
                    ["remoteip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty
                }));

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>();
            return payload?.Success == true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GenerateUniqueCertificateNumberAsync(string certificatePrefix, HashSet<string>? reservedValues = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var value = _certificateSecurity.GenerateCertificateNumber(certificatePrefix);
            if (reservedValues != null && reservedValues.Contains(value))
                continue;

            var exists = await _db.Certificates.AnyAsync(c => c.CertificateNumber == value);
            if (exists) continue;

            reservedValues?.Add(value);
            return value;
        }

        throw new InvalidOperationException("تعذر توليد رقم شهادة فريد.");
    }

    private async Task<string> GenerateUniquePublicIdAsync(HashSet<string>? reservedValues = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var value = _certificateSecurity.GeneratePublicId();
            if (reservedValues != null && reservedValues.Contains(value))
                continue;

            var exists = await _db.Certificates.AnyAsync(c => c.PublicId == value);
            if (exists) continue;

            reservedValues?.Add(value);
            return value;
        }

        throw new InvalidOperationException("تعذر توليد معرف عام فريد.");
    }

    private async Task<string> GenerateUniqueVerificationCodeAsync(HashSet<string>? reservedValues = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var value = _certificateSecurity.GenerateVerificationCode();
            if (reservedValues != null && reservedValues.Contains(value))
                continue;

            var exists = await _db.Certificates.AnyAsync(c => c.VerificationCode == value);
            if (exists) continue;

            reservedValues?.Add(value);
            return value;
        }

        throw new InvalidOperationException("تعذر توليد رمز تحقق فريد.");
    }

    private static string NormalizeLookupCode(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string MapCertificateStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "valid" => "سارية",
            "revoked" => "ملغية",
            _ => "غير معروفة"
        };

    private sealed class TurnstileVerifyResponse
    {
        public bool Success { get; set; }
    }
}
