using ECert.Data;
using ECert.Models;
using ECert.Models.ViewModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using ECert.ViewModels;
using System.Globalization;
using System.Text.Json;

namespace ECert.Controllers;

[Authorize]
public class RegistrationsController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly NotificationService _notify;
    private readonly RegistrationInvoiceService _invoiceService;

    public RegistrationsController(ECertDbContext db, AuditLogService audit, NotificationService notify, RegistrationInvoiceService invoiceService)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
        _invoiceService = invoiceService;
    }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    private string? SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private IActionResult ReturnToList(string? returnUrl) =>
        SafeReturnUrl(returnUrl) is { } safeUrl ? LocalRedirect(safeUrl) : RedirectToAction(nameof(Index));

    public async Task<IActionResult> Index(string? status, string? search, int? courseId, DateTime? dateFrom, DateTime? dateTo)
    {
        if (!HasPermission("manage-registrations")) return Forbid();

        var query = _db.Registrations
            .Include(r => r.Course)
            .Include(r => r.Invoice)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => (r.FullNameArabic != null && r.FullNameArabic.Contains(search)) || (r.FullNameEnglish != null && r.FullNameEnglish.Contains(search)) || r.FullName.Contains(search) || r.Phone.Contains(search) || r.RequestNumber.Contains(search));
        if (courseId.HasValue) query = query.Where(r => r.CourseId == courseId.Value);
        if (dateFrom.HasValue) query = query.Where(r => r.RegistrationDate >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(r => r.RegistrationDate <= dateTo.Value.AddDays(1));

        var registrations = await query
            .OrderByDescending(r => r.RegistrationDate)
            .ToListAsync();

        ViewBag.Registrations = registrations;
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.CourseId = courseId;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        // Pass the public Course entity rather than an anonymous projection.
        // Razor iterates ViewBag values as dynamic; anonymous-type properties are
        // inaccessible to the generated view and cause RuntimeBinderException.
        ViewBag.Courses = await _db.Courses
            .OrderBy(c => c.CourseName)
            .ToListAsync();
        ViewBag.TotalCount = registrations.Count;
        ViewBag.PendingCount = registrations.Count(r => r.Status == "Pending");
        ViewBag.ReturnUrl = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
        return View();
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations
            .Include(r => r.Course)
            .Include(r => r.Invoice).ThenInclude(i => i!.Payments)
            .FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();
        return View(reg);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id, string? returnUrl)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();

        if (reg.Status != "Pending")
        {
            TempData["Error"] = "لا يمكن قبول هذا الطلب لأنه تمت معالجته بالفعل.";
            return ReturnToList(returnUrl);
        }

        reg.Status = "Accepted";
        reg.AcceptedDate = DateTime.Now;
        reg.ProcessedBy = User.Identity?.Name ?? "System";
        var invoice = await _invoiceService.EnsureForAcceptedAsync(reg, reg.ProcessedBy);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Accept", "Registration", id, null, $"Status: Accepted; Invoice: {invoice.InvoiceNumber}");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "RegistrationAccepted",
            $"عزيزي {reg.FullName}، تم قبولك في دورة {reg.Course?.CourseName}. سيتم التواصل معك قريباً.", "SMS", "Registration", id);

        TempData["Success"] = $"تم قبول طلب {reg.FullName} بنجاح وإنشاء الفاتورة {invoice.InvoiceNumber}.";
        return ReturnToList(returnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Reject(int id, string? returnUrl)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();

        if (reg.Status != "Pending")
        {
            TempData["Error"] = "لا يمكن رفض هذا الطلب لأنه تمت معالجته بالفعل.";
            return ReturnToList(returnUrl);
        }

        return View(new RejectRegistrationViewModel
        {
            RegistrationId = id,
            ReturnUrl = SafeReturnUrl(returnUrl)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(RejectRegistrationViewModel model)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == model.RegistrationId);
        if (reg == null) return NotFound();

        if (reg.Status != "Pending")
        {
            TempData["Error"] = "لم يُنفذ الرفض لأن حالة الطلب تغيرت بالفعل.";
            return ReturnToList(model.ReturnUrl);
        }

        reg.Status = "Rejected";
        reg.RejectionReason = model.Reason;
        reg.RejectedDate = DateTime.Now;
        reg.ProcessedBy = User.Identity?.Name ?? "System";

        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Reject", "Registration", model.RegistrationId, null, $"Reason: {model.Reason}");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "RegistrationRejected",
            $"عزيزي {reg.FullName}، نأسف لإبلاغك بأن طلب تسجيلك في دورة {reg.Course?.CourseName} لم يتم قبوله. السبب: {model.Reason}", "SMS", "Registration", model.RegistrationId);

        TempData["Success"] = "تم رفض الطلب بنجاح.";
        return ReturnToList(model.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, string? returnUrl)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();

        if (reg.Status != "Rejected")
        {
            TempData["Error"] = "يمكن إعادة فتح الطلبات المرفوضة فقط.";
            return ReturnToList(returnUrl);
        }

        reg.Status = "Pending";
        reg.ReopenedDate = DateTime.Now;
        reg.RejectionReason = null;
        reg.RejectedDate = null;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Reopen", "Registration", id, null, "Reopened to Pending");
        TempData["Success"] = "تمت إعادة فتح الطلب.";
        return ReturnToList(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyExemption(ApplyExemptionViewModel model)
    {
        if (!HasPermission("manage-registrations")) return Forbid();

        var reg = await _db.Registrations
            .Include(r => r.Course)
            .Include(r => r.Invoice)
            .FirstOrDefaultAsync(r => r.RegistrationId == model.RegistrationId);
        if (reg == null) return NotFound();

        if (reg.Status is not ("Pending" or "Accepted"))
        {
            TempData["Error"] = "يمكن تطبيق الإعفاء على التسجيلات المعلقة أو المقبولة فقط.";
            return RedirectToAction(nameof(Details), new { id = model.RegistrationId });
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id = model.RegistrationId });
        }

        try
        {
            await _invoiceService.ApplyExemptionAsync(reg, model.ExemptionAmount, model.Reason, User.Identity?.Name ?? "System");
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.Identity?.Name ?? "", "ApplyExemption", "Registration", reg.RegistrationId, null,
                $"Exemption: {reg.ExemptionAmount}; Reason: {reg.ExemptionReason ?? "-"}");
            TempData["Success"] = reg.ExemptionAmount > 0
                ? $"تم تطبيق إعفاء بقيمة {reg.ExemptionAmount:N2} ريال بنجاح."
                : "تم إلغاء الإعفاء وإعادة الرسوم إلى قيمتها الأصلية.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = model.RegistrationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, string? returnUrl)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.FindAsync(id);
        if (reg == null) return NotFound();

        if (reg.Status is not ("Accepted" or "Rejected"))
        {
            TempData["Error"] = "يمكن أرشفة الطلبات المقبولة أو المرفوضة فقط.";
            return ReturnToList(returnUrl);
        }

        reg.Status = "Archived";
        reg.ArchivedDate = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Archive", "Registration", id);
        TempData["Success"] = "تمت أرشفة الطلب.";
        return ReturnToList(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, string? returnUrl)
    {
        if (!HasPermission("manage-registrations")) return Forbid();

        var reg = await _db.Registrations
            .Include(r => r.Invoice)
            .FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();

        if (reg.Status != "Archived")
        {
            TempData["Error"] = "يمكن استعادة الطلبات المؤرشفة فقط.";
            return ReturnToList(returnUrl);
        }

        // Archived records created by the previous workflow do not store the prior status.
        // Use the persisted acceptance/invoice data to restore the most likely original state.
        reg.Status = reg.AcceptedDate.HasValue || reg.Invoice != null ? "Accepted" : "Rejected";
        reg.ArchivedDate = null;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Restore", "Registration", id, null, $"Restored to: {reg.Status}");
        TempData["Success"] = reg.Status == "Accepted"
            ? "تمت استعادة الطلب إلى قائمة المقبولين."
            : "تمت استعادة الطلب إلى قائمة المرفوضين، ويمكن إعادة فتحه عند الحاجة.";
        return ReturnToList(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl)
    {
        var isSuperAdmin = User.HasClaim(c => c.Type == "Role" && c.Value == "SuperAdmin");
        if (!isSuperAdmin) return Forbid();

        var reg = await _db.Registrations.FindAsync(id);
        if (reg == null) return NotFound();

        _db.Registrations.Remove(reg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "Registration", id, null, $"Permanently deleted: {reg.FullName}");
        TempData["Success"] = "تم حذف الطلب نهائياً.";
        return ReturnToList(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAction(int[] ids, string action, string? note, string? returnUrl)
    {
        if (!HasPermission("manage-registrations")) return Forbid();

        var selectedIds = (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToArray();
        var selectedIdList = selectedIds.ToList();
        if (selectedIds.Length == 0)
        {
            TempData["Error"] = "اختر طلباً معلقاً واحداً على الأقل قبل تنفيذ الإجراء.";
            return ReturnToList(returnUrl);
        }

        var actionKey = action?.Trim().ToLowerInvariant();
        if (actionKey is not ("approve" or "reject"))
        {
            TempData["Error"] = "الإجراء المطلوب غير صالح.";
            return ReturnToList(returnUrl);
        }

        var selectedRegistrations = await _db.Registrations
            .Include(r => r.Course)
            .Include(r => r.Invoice)
            .Where(r => selectedIdList.Contains(r.RegistrationId))
            .ToListAsync();

        // A bulk decision is intentionally limited to pending registrations. This prevents
        // already accepted/rejected/archived records from being changed by a stale selection.
        var eligible = selectedRegistrations.Where(r => r.Status == "Pending").ToList();
        var skipped = selectedIds.Length - eligible.Count;

        if (eligible.Count == 0)
        {
            TempData["Error"] = "لم يُنفذ أي إجراء؛ الطلبات المحددة تمت معالجتها مسبقاً أو لم تعد مؤهلة.";
            return ReturnToList(returnUrl);
        }

        var now = DateTime.Now;
        var processedBy = User.Identity?.Name ?? "System";
        var createdInvoiceNumbers = new List<string>();
        if (actionKey == "approve")
        {
            foreach (var registration in eligible)
            {
                registration.Status = "Accepted";
                registration.AcceptedDate = now;
                registration.ProcessedBy = processedBy;
                var invoice = await _invoiceService.EnsureForAcceptedAsync(registration, processedBy);
                createdInvoiceNumbers.Add(invoice.InvoiceNumber);
            }
        }
        else
        {
            foreach (var registration in eligible)
            {
                registration.Status = "Rejected";
                registration.RejectionReason = string.IsNullOrWhiteSpace(note) ? "تم الرفض ضمن إجراء جماعي" : note.Trim();
                registration.RejectedDate = now;
                registration.ProcessedBy = processedBy;
            }
        }

        await _db.SaveChangesAsync();

        // Keep the audit payload short and deterministic. A long list of invoice numbers
        // can exceed legacy column limits and turn a successful batch into an error page.
        var invoiceSummary = createdInvoiceNumbers.Count == 0
            ? "none"
            : $"{createdInvoiceNumbers.Count} invoice(s) created";
        await _audit.LogAsync(User.Identity?.Name ?? "", "BulkAction", "Registration", null, null,
            $"Action: {actionKey}; Changed: {eligible.Count}; Skipped: {skipped}; Invoices: {invoiceSummary}");

        var actionLabel = actionKey == "approve" ? "قبول وإنشاء الفواتير" : "رفض";
        TempData["Success"] = skipped > 0
            ? $"تم {actionLabel} {eligible.Count} طلب/طلبات معلقة، وتجاوز {skipped} طلب/طلبات تمت معالجتها مسبقاً."
            : $"تم {actionLabel} {eligible.Count} طلب/طلبات معلقة بنجاح.";
        return ReturnToList(returnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var model = await BuildRegistrationImportPageViewModelAsync(new RegistrationImportPageViewModel());
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadImportTemplate(int? courseId)
    {
        if (!HasPermission("manage-registrations")) return Forbid();

        var course = courseId.HasValue
            ? await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.CourseId == courseId.Value)
            : null;
        var requiresAcademic = course?.RequiresAcademicDetails == true;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Registrations");
        var headers = new List<string>
        {
            "FullNameArabic", "FullNameEnglish", "Gender", "Phone", "Country", "Email", "HeardFrom"
        };
        if (requiresAcademic)
            headers.AddRange(new[] { "University", "College", "Specialization", "AcademicLevel" });

        for (var i = 0; i < headers.Count; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        sheet.Row(1).Style.Font.FontColor = XLColor.White;
        sheet.Cell(2, 1).Value = "محمد أحمد";
        sheet.Cell(2, 2).Value = "Mohammed Ahmed";
        sheet.Cell(2, 3).Value = "Male";
        sheet.Cell(2, 4).Value = "0551234567";
        sheet.Cell(2, 5).Value = "السعودية";
        sheet.Cell(2, 6).Value = "m.ahmed@example.com";
        sheet.Cell(2, 7).Value = "تسجيل حضوري";
        if (requiresAcademic)
        {
            sheet.Cell(2, 8).Value = "اسم الجامعة كما هو في النظام";
            sheet.Cell(2, 9).Value = "اسم الكلية كما هو في النظام";
            sheet.Cell(2, 10).Value = "اسم التخصص كما هو في النظام";
            sheet.Cell(2, 11).Value = "المستوى الدراسي كما هو في النظام";
        }
        sheet.Row(2).Style.Font.FontColor = XLColor.Gray;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = course == null
            ? "registrations-import-template.xlsx"
            : $"registrations-import-template-course-{course.CourseId}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(RegistrationImportPageViewModel model)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        model = await BuildRegistrationImportPageViewModelAsync(model);

        if (!model.CourseId.HasValue)
        {
            ModelState.AddModelError(nameof(model.CourseId), "اختر الدورة أولاً.");
            return View(model);
        }

        var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.CourseId == model.CourseId.Value);
        if (course == null)
        {
            ModelState.AddModelError(nameof(model.CourseId), "الدورة المحددة غير موجودة.");
            return View(model);
        }

        if (model.ImportStatus is not ("Pending" or "Accepted"))
        {
            ModelState.AddModelError(nameof(model.ImportStatus), "اختر حالة صالحة بعد الاستيراد.");
            return View(model);
        }

        if (model.File == null || model.File.Length == 0)
        {
            ModelState.AddModelError(nameof(model.File), "اختر ملف XLSX صالحاً.");
            return View(model);
        }

        if (!string.Equals(Path.GetExtension(model.File.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase) || model.File.Length > 10_000_000)
        {
            ModelState.AddModelError(nameof(model.File), "يسمح فقط بملفات XLSX بحجم لا يتجاوز 10 ميجابايت.");
            return View(model);
        }

        try
        {
            await using var stream = model.File.OpenReadStream();
            var preview = await BuildRegistrationImportPreviewAsync(stream, course, model.ImportStatus);
            var token = Guid.NewGuid().ToString("N");
            preview.Token = token;
            preview.CreatedAtUtc = DateTime.UtcNow;
            var previewDir = Path.Combine(Path.GetTempPath(), "ecert-registration-imports");
            Directory.CreateDirectory(previewDir);
            await System.IO.File.WriteAllTextAsync(Path.Combine(previewDir, token + ".json"), JsonSerializer.Serialize(preview));
            model.PreviewToken = token;
            model.Preview = preview;
            return View(model);
        }
        catch (Exception)
        {
            ModelState.AddModelError(nameof(model.File), "تعذر قراءة الملف. تأكد أنه ملف XLSX سليم وغير محمي بكلمة مرور.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmImport(string token)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32) return BadRequest("رمز المعاينة غير صالح.");

        var previewPath = Path.Combine(Path.GetTempPath(), "ecert-registration-imports", token + ".json");
        if (!System.IO.File.Exists(previewPath))
        {
            TempData["Error"] = "انتهت صلاحية المعاينة. ارفع الملف مرة أخرى.";
            return RedirectToAction(nameof(Import));
        }

        RegistrationImportPreview? preview;
        try
        {
            preview = JsonSerializer.Deserialize<RegistrationImportPreview>(await System.IO.File.ReadAllTextAsync(previewPath));
        }
        finally
        {
            try { System.IO.File.Delete(previewPath); } catch { }
        }

        if (preview == null || preview.CreatedAtUtc < DateTime.UtcNow.AddHours(-1) || preview.InvalidRows > 0 || preview.ValidRows == 0)
        {
            TempData["Error"] = "لا يمكن اعتماد الملف. يجب معالجة جميع الأخطاء أولاً.";
            return RedirectToAction(nameof(Import));
        }

        var course = await _db.Courses.FirstOrDefaultAsync(c => c.CourseId == preview.CourseId);
        if (course == null)
        {
            TempData["Error"] = "الدورة المحددة لم تعد موجودة.";
            return RedirectToAction(nameof(Import));
        }

        var duplicatePhones = preview.Rows
            .Select(r => r.NormalizedPhone)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingDuplicates = duplicatePhones.Count == 0
            ? new List<string>()
            : await _db.Registrations
                .Where(r => r.CourseId == preview.CourseId
                    && duplicatePhones.Contains(r.Phone)
                    && r.Status != "Rejected"
                    && r.Status != "Archived")
                .Select(r => r.Phone)
                .Distinct()
                .ToListAsync();

        if (existingDuplicates.Count > 0)
        {
            TempData["Error"] = "يوجد متدربون مسجلون مسبقاً بنفس أرقام الجوال داخل الدورة. لم يتم حفظ أي صف.";
            return RedirectToAction(nameof(Import));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var processedBy = User.Identity?.Name ?? "System";
            var imported = new List<Registration>();

            foreach (var row in preview.Rows)
            {
                var registration = new Registration
                {
                    RequestNumber = await GenerateRequestNumberAsync(),
                    FullName = row.FullNameArabic,
                    FullNameArabic = row.FullNameArabic,
                    FullNameEnglish = row.FullNameEnglish,
                    Gender = row.Gender,
                    Phone = row.NormalizedPhone!,
                    Email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
                    HeardFrom = string.IsNullOrWhiteSpace(row.HeardFrom) ? "استيراد Excel" : row.HeardFrom.Trim(),
                    CourseId = preview.CourseId,
                    Status = preview.ImportStatus,
                    RegistrationDate = now,
                    AcceptedDate = preview.ImportStatus == "Accepted" ? now : null,
                    ProcessedBy = preview.ImportStatus == "Accepted" ? processedBy : null,
                    UniversityId = row.UniversityId,
                    CollegeId = row.CollegeId,
                    AcademicSpecializationId = row.AcademicSpecializationId,
                    AcademicLevel = string.IsNullOrWhiteSpace(row.AcademicLevel) ? null : row.AcademicLevel.Trim(),
                    UniversityNameSnapshot = string.IsNullOrWhiteSpace(row.University) ? null : row.University.Trim(),
                    CollegeNameSnapshot = string.IsNullOrWhiteSpace(row.College) ? null : row.College.Trim(),
                    SpecializationNameSnapshot = string.IsNullOrWhiteSpace(row.Specialization) ? null : row.Specialization.Trim()
                };
                _db.Registrations.Add(registration);
                imported.Add(registration);
            }

            await _db.SaveChangesAsync();

            if (preview.ImportStatus == "Accepted")
            {
                foreach (var registration in imported)
                {
                    registration.Course = course;
                    await _invoiceService.EnsureForAcceptedAsync(registration, processedBy);
                }
                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            await _audit.LogAsync(User.Identity?.Name ?? "", "BulkCreate", "Registration", null, null,
                $"Imported {preview.ValidRows} registrations into course {preview.CourseId} with status {preview.ImportStatus}");
            TempData["Success"] = preview.ImportStatus == "Accepted"
                ? $"تم استيراد {preview.ValidRows} متدرباً واعتمادهم مباشرة وإنشاء الفواتير اللازمة."
                : $"تم استيراد {preview.ValidRows} متدرباً كطلبات معلقة بنجاح.";
        }
        catch
        {
            await transaction.RollbackAsync();
            TempData["Error"] = "فشل حفظ الاستيراد بالكامل، ولم يتم حفظ أي متدرب.";
        }

        return RedirectToAction(nameof(Index), new { courseId = preview.CourseId });
    }

    private async Task<RegistrationImportPageViewModel> BuildRegistrationImportPageViewModelAsync(RegistrationImportPageViewModel model)
    {
        model.Courses = await _db.Courses
            .Where(c => c.Status != "Archived")
            .OrderBy(c => c.CourseNameArabic ?? c.CourseName)
            .Select(c => new RegistrationImportCourseOption
            {
                CourseId = c.CourseId,
                CourseName = (c.CourseNameArabic ?? c.CourseName) + (c.CourseNameEnglish != null ? $" / {c.CourseNameEnglish}" : string.Empty),
                RequiresAcademicDetails = c.RequiresAcademicDetails
            })
            .ToListAsync();

        if (model.CourseId.HasValue)
            model.CourseRequiresAcademicDetails = model.Courses.FirstOrDefault(c => c.CourseId == model.CourseId.Value)?.RequiresAcademicDetails == true;

        return model;
    }

    private async Task<RegistrationImportPreview> BuildRegistrationImportPreviewAsync(Stream stream, Course course, string importStatus)
    {
        var preview = new RegistrationImportPreview
        {
            CourseId = course.CourseId,
            CourseName = course.CourseNameArabic ?? course.CourseName,
            ImportStatus = importStatus,
            CourseRequiresAcademicDetails = course.RequiresAcademicDetails
        };

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException();
        var used = sheet.RangeUsed() ?? throw new InvalidDataException();
        if (used.RowCount() < 2 || used.ColumnCount() < 4) throw new InvalidDataException();
        if (used.RowCount() > 5001) throw new InvalidDataException("The workbook exceeds the 5000-row limit.");

        var headers = Enumerable.Range(1, used.ColumnCount()).ToDictionary(i => CanonicalRegistrationHeader(sheet.Cell(1, i).GetString()), i => i);
        var required = new[] { "fullnamearabic", "fullnameenglish", "gender", "phone" };
        var missing = required.Where(h => !headers.ContainsKey(h)).ToList();
        if (missing.Count > 0) throw new InvalidDataException($"Missing columns: {string.Join(",", missing)}");

        var countries = await _db.PhoneCountries.Where(c => c.IsActive).ToListAsync();
        var countryByName = countries.ToDictionary(c => NormalizeLookup(c.CountryName), c => c);
        var countryByCode = countries.ToDictionary(c => NormalizeLookup(c.CountryCode), c => c);

        var universities = await _db.Universities.Where(u => u.IsActive).ToListAsync();
        var colleges = await _db.Colleges.Where(c => c.IsActive).ToListAsync();
        var specializations = await _db.AcademicSpecializations.Where(s => s.IsActive).ToListAsync();
        var levelOptions = await _db.AcademicLevelOptions.Where(l => l.IsActive).ToListAsync();

        var seenPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var rowNumber = 2; rowNumber <= used.LastRow().RowNumber(); rowNumber++)
        {
            var row = new RegistrationImportRow { RowNumber = rowNumber };
            row.FullNameArabic = RegistrationText(sheet, headers, rowNumber, "fullnamearabic");
            row.FullNameEnglish = RegistrationText(sheet, headers, rowNumber, "fullnameenglish");
            row.Gender = RegistrationOptionalText(sheet, headers, rowNumber, "gender");
            row.Phone = RegistrationText(sheet, headers, rowNumber, "phone");
            row.Country = RegistrationOptionalText(sheet, headers, rowNumber, "country");
            row.Email = RegistrationOptionalText(sheet, headers, rowNumber, "email");
            row.HeardFrom = RegistrationOptionalText(sheet, headers, rowNumber, "heardfrom");
            row.University = RegistrationOptionalText(sheet, headers, rowNumber, "university");
            row.College = RegistrationOptionalText(sheet, headers, rowNumber, "college");
            row.Specialization = RegistrationOptionalText(sheet, headers, rowNumber, "specialization");
            row.AcademicLevel = RegistrationOptionalText(sheet, headers, rowNumber, "academiclevel");

            if (string.IsNullOrWhiteSpace(row.FullNameArabic)) row.Errors.Add("الاسم بالعربية مطلوب");
            if (string.IsNullOrWhiteSpace(row.FullNameEnglish)) row.Errors.Add("الاسم بالإنجليزية مطلوب");
            if (row.FullNameArabic.Length > 100) row.Errors.Add("الاسم بالعربية يتجاوز 100 حرف");
            if (row.FullNameEnglish.Length > 100) row.Errors.Add("الاسم بالإنجليزية يتجاوز 100 حرف");
            if (row.Gender is not ("Male" or "Female" or "ذكر" or "أنثى")) row.Errors.Add("الجنس يجب أن يكون Male أو Female");

            var phoneResult = NormalizeImportedPhone(row.Phone, row.Country, countryByName, countryByCode);
            if (!phoneResult.IsValid)
            {
                row.Errors.Add(phoneResult.ErrorMessage!);
            }
            else
            {
                row.NormalizedPhone = phoneResult.NormalizedPhone;
                if (!seenPhones.Add(row.NormalizedPhone!))
                    row.Errors.Add("رقم الهاتف مكرر داخل الملف لنفس الدورة");
            }

            if (!string.IsNullOrWhiteSpace(row.Email) && !row.Email.Contains('@'))
                row.Errors.Add("البريد الإلكتروني غير صحيح");

            var requiresAcademic = course.RequiresAcademicDetails
                || !string.IsNullOrWhiteSpace(row.University)
                || !string.IsNullOrWhiteSpace(row.College)
                || !string.IsNullOrWhiteSpace(row.Specialization)
                || !string.IsNullOrWhiteSpace(row.AcademicLevel);

            if (requiresAcademic)
            {
                if (string.IsNullOrWhiteSpace(row.University)) row.Errors.Add("اسم الجامعة مطلوب لهذه الدورة");
                if (string.IsNullOrWhiteSpace(row.College)) row.Errors.Add("اسم الكلية مطلوب لهذه الدورة");
                if (string.IsNullOrWhiteSpace(row.Specialization)) row.Errors.Add("اسم التخصص مطلوب لهذه الدورة");
                if (string.IsNullOrWhiteSpace(row.AcademicLevel)) row.Errors.Add("المستوى الدراسي مطلوب لهذه الدورة");

                var university = universities.FirstOrDefault(u => NormalizeLookup(u.UniversityName) == NormalizeLookup(row.University));
                if (university == null)
                {
                    row.Errors.Add("الجامعة غير موجودة أو غير نشطة");
                }
                else
                {
                    row.UniversityId = university.UniversityId;
                    var college = colleges.FirstOrDefault(c => c.UniversityId == university.UniversityId && NormalizeLookup(c.CollegeName) == NormalizeLookup(row.College));
                    if (college == null)
                    {
                        row.Errors.Add("الكلية غير موجودة أو لا تنتمي إلى الجامعة المحددة");
                    }
                    else
                    {
                        row.CollegeId = college.CollegeId;
                        var specialization = specializations.FirstOrDefault(s => s.CollegeId == college.CollegeId && NormalizeLookup(s.SpecializationName) == NormalizeLookup(row.Specialization));
                        if (specialization == null)
                        {
                            row.Errors.Add("التخصص غير موجود أو لا ينتمي إلى الكلية المحددة");
                        }
                        else
                        {
                            row.AcademicSpecializationId = specialization.AcademicSpecializationId;
                            var levelMatch = levelOptions.Any(l => l.AcademicSpecializationId == specialization.AcademicSpecializationId && NormalizeLookup(l.LevelName) == NormalizeLookup(row.AcademicLevel));
                            if (!levelMatch)
                                row.Errors.Add("المستوى الدراسي غير متاح لهذا التخصص");
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(row.NormalizedPhone))
            {
                var duplicateExists = await _db.Registrations.AsNoTracking().AnyAsync(r =>
                    r.CourseId == course.CourseId
                    && r.Phone == row.NormalizedPhone
                    && r.Status != "Rejected"
                    && r.Status != "Archived");
                if (duplicateExists)
                    row.Errors.Add("يوجد تسجيل قائم بهذا الرقم لهذه الدورة بالفعل");
            }

            preview.Rows.Add(row);
        }

        preview.TotalRows = preview.Rows.Count;
        preview.ValidRows = preview.Rows.Count(r => r.IsValid);
        preview.InvalidRows = preview.Rows.Count - preview.ValidRows;
        return preview;
    }

    private async Task<string> GenerateRequestNumberAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"REG-{DateTime.Now:yyyy}-{Random.Shared.Next(10000, 99999)}";
            if (!await _db.Registrations.AnyAsync(r => r.RequestNumber == candidate))
                return candidate;
        }

        return $"REG-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..36];
    }

    private static string CanonicalRegistrationHeader(string value)
    {
        var header = NormalizeLookup(value).Replace(" ", string.Empty).Replace("_", string.Empty);
        return header switch
        {
            "الاسمالعربي" or "اسمالمتدرببالعربية" or "fullnamearabic" => "fullnamearabic",
            "الاسمالانجليزي" or "اسمالمتدرببالانجليزية" or "fullnameenglish" => "fullnameenglish",
            "الجنس" or "gender" => "gender",
            "الهاتف" or "رقمالهاتف" or "phone" => "phone",
            "الدولة" or "مفتاحالدولة" or "country" or "countrycode" => "country",
            "البريدالالكتروني" or "email" => "email",
            "كيفسمعتعنا" or "heardfrom" => "heardfrom",
            "الجامعة" or "university" => "university",
            "الكلية" or "college" => "college",
            "التخصص" or "specialization" => "specialization",
            "المستوىالدراسي" or "academiclevel" => "academiclevel",
            _ => header
        };
    }

    private static string RegistrationText(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key)
        => headers.TryGetValue(key, out var col) ? sheet.Cell(row, col).GetString().Trim() : string.Empty;

    private static string? RegistrationOptionalText(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key)
    {
        var value = RegistrationText(sheet, headers, row, key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string NormalizeLookup(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static (bool IsValid, string? NormalizedPhone, string? ErrorMessage) NormalizeImportedPhone(
        string rawPhone,
        string? rawCountry,
        IReadOnlyDictionary<string, PhoneCountry> countryByName,
        IReadOnlyDictionary<string, PhoneCountry> countryByCode)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return (false, null, "رقم الهاتف مطلوب");

        var phone = rawPhone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        PhoneCountry? country = null;
        if (!string.IsNullOrWhiteSpace(rawCountry))
        {
            var key = NormalizeLookup(rawCountry);
            if (!countryByName.TryGetValue(key, out country) && !countryByCode.TryGetValue(key, out country))
                return (false, null, "الدولة/مفتاح الدولة غير موجود في الإعدادات");
        }

        if (phone.StartsWith('+'))
        {
            if (phone.Count(ch => ch == '+') > 1 || !phone[1..].All(char.IsDigit))
                return (false, null, "رقم الهاتف الدولي غير صالح");
            if (country != null && !phone.StartsWith(country.CountryCode, StringComparison.OrdinalIgnoreCase))
                return (false, null, "رقم الهاتف لا يطابق مفتاح الدولة المحدد");
            return (true, phone, null);
        }

        if (country == null)
            return (false, null, "أدخل الدولة أو استخدم رقم هاتف يبدأ بـ +");

        if (phone.StartsWith('0'))
            phone = phone[1..];
        if (!phone.All(char.IsDigit))
            return (false, null, "رقم الهاتف يجب أن يحتوي على أرقام فقط");
        if (phone.Length < country.MinPhoneLength || phone.Length > country.MaxPhoneLength)
            return (false, null, $"رقم الهاتف يجب أن يكون بين {country.MinPhoneLength} و {country.MaxPhoneLength} أرقام");

        if (!string.IsNullOrWhiteSpace(country.Prefixes))
        {
            var prefixes = country.Prefixes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (prefixes.Length > 0 && !prefixes.Any(phone.StartsWith))
                return (false, null, $"رقم الهاتف يجب أن يبدأ بأحد البادئات: {string.Join(", ", prefixes)}");
        }

        return (true, $"{country.CountryCode}{phone}", null);
    }

    public async Task<IActionResult> ExportXlsx(string? status, string? search, int? courseId, DateTime? dateFrom, DateTime? dateTo)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var query = _db.Registrations.Include(r => r.Course).Include(r => r.Invoice).AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => (r.FullNameArabic != null && r.FullNameArabic.Contains(search)) || (r.FullNameEnglish != null && r.FullNameEnglish.Contains(search)) || r.FullName.Contains(search) || r.Phone.Contains(search) || r.RequestNumber.Contains(search));
        if (courseId.HasValue) query = query.Where(r => r.CourseId == courseId.Value);
        if (dateFrom.HasValue) query = query.Where(r => r.RegistrationDate >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(r => r.RegistrationDate <= dateTo.Value.AddDays(1));
        var list = await query.OrderByDescending(r => r.RegistrationDate).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("التسجيلات");
        ws.Cell(1, 1).Value = "رقم الطلب";
        ws.Cell(1, 2).Value = "الاسم بالعربية";
        ws.Cell(1, 3).Value = "الاسم بالإنجليزية";
        ws.Cell(1, 4).Value = "الجنس";
        ws.Cell(1, 5).Value = "الهاتف";
        ws.Cell(1, 6).Value = "الدورة";
        ws.Cell(1, 7).Value = "التاريخ";
        ws.Cell(1, 8).Value = "الحالة";
        ws.Cell(1, 9).Value = "الموظف";
        int row = 2;
        foreach (var r in list)
        {
            ws.Cell(row, 1).Value = r.RequestNumber;
            ws.Cell(row, 2).Value = r.FullNameArabic ?? r.FullName;
            ws.Cell(row, 3).Value = r.FullNameEnglish ?? r.FullName;
            ws.Cell(row, 4).Value = r.Gender == "Male" ? "ذكر" : r.Gender == "Female" ? "أنثى" : "";
            ws.Cell(row, 5).Value = r.Phone;
            ws.Cell(row, 6).Value = r.Course?.CourseName ?? "";
            ws.Cell(row, 7).Value = r.RegistrationDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 8).Value = r.Status;
            ws.Cell(row, 9).Value = r.ProcessedBy ?? "";
            row++;
        }
        ws.Columns().AdjustToContents();
        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"registrations_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
