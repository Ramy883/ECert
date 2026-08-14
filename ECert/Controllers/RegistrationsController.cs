using ECert.Data;
using ECert.Models;
using ECert.Models.ViewModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace ECert.Controllers;

[Authorize]
public class RegistrationsController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly NotificationService _notify;

    public RegistrationsController(ECertDbContext db, AuditLogService audit, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
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
            query = query.Where(r => r.FullName.Contains(search) || r.Phone.Contains(search) || r.RequestNumber.Contains(search));
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
        ViewBag.Courses = await _db.Courses
            .OrderBy(c => c.CourseName)
            .Select(c => new { c.CourseId, c.CourseName })
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
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Accept", "Registration", id, null, "Status: Accepted");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "RegistrationAccepted",
            $"عزيزي {reg.FullName}، تم قبولك في دورة {reg.Course?.CourseName}. سيتم التواصل معك قريباً.", "SMS", "Registration", id);

        TempData["Success"] = $"تم قبول طلب {reg.FullName} بنجاح.";
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

        var course = await _db.Courses.FindAsync(reg.CourseId);
        if (course != null && course.BookedSeats > 0) course.BookedSeats--;

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

        var course = await _db.Courses.FindAsync(reg.CourseId);
        if (course != null) course.BookedSeats++;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Reopen", "Registration", id, null, "Reopened to Pending");
        TempData["Success"] = "تمت إعادة فتح الطلب.";
        return ReturnToList(returnUrl);
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
            .Where(r => selectedIds.Contains(r.RegistrationId))
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
        if (actionKey == "approve")
        {
            foreach (var registration in eligible)
            {
                registration.Status = "Accepted";
                registration.AcceptedDate = now;
                registration.ProcessedBy = processedBy;
            }
        }
        else
        {
            var courseIds = eligible.Select(r => r.CourseId).Distinct().ToArray();
            var coursesById = await _db.Courses
                .Where(c => courseIds.Contains(c.CourseId))
                .ToDictionaryAsync(c => c.CourseId);

            foreach (var registration in eligible)
            {
                registration.Status = "Rejected";
                registration.RejectionReason = string.IsNullOrWhiteSpace(note) ? "تم الرفض ضمن إجراء جماعي" : note.Trim();
                registration.RejectedDate = now;
                registration.ProcessedBy = processedBy;

                if (coursesById.TryGetValue(registration.CourseId, out var course) && course.BookedSeats > 0)
                    course.BookedSeats--;
            }
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "BulkAction", "Registration", null, null,
            $"Action: {actionKey}; Changed: {eligible.Count}; Skipped: {skipped}");

        var actionLabel = actionKey == "approve" ? "قبول" : "رفض";
        TempData["Success"] = skipped > 0
            ? $"تم {actionLabel} {eligible.Count} طلب/طلبات معلقة، وتجاوز {skipped} طلب/طلبات تمت معالجتها مسبقاً."
            : $"تم {actionLabel} {eligible.Count} طلب/طلبات معلقة بنجاح.";
        return ReturnToList(returnUrl);
    }

    public async Task<IActionResult> ExportXlsx(string? status, string? search, int? courseId, DateTime? dateFrom, DateTime? dateTo)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var query = _db.Registrations.Include(r => r.Course).Include(r => r.Invoice).AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.FullName.Contains(search) || r.Phone.Contains(search) || r.RequestNumber.Contains(search));
        if (courseId.HasValue) query = query.Where(r => r.CourseId == courseId.Value);
        if (dateFrom.HasValue) query = query.Where(r => r.RegistrationDate >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(r => r.RegistrationDate <= dateTo.Value.AddDays(1));
        var list = await query.OrderByDescending(r => r.RegistrationDate).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("التسجيلات");
        ws.Cell(1, 1).Value = "رقم الطلب";
        ws.Cell(1, 2).Value = "الاسم";
        ws.Cell(1, 3).Value = "الهاتف";
        ws.Cell(1, 4).Value = "الدورة";
        ws.Cell(1, 5).Value = "التاريخ";
        ws.Cell(1, 6).Value = "الحالة";
        ws.Cell(1, 7).Value = "الموظف";
        int row = 2;
        foreach (var r in list)
        {
            ws.Cell(row, 1).Value = r.RequestNumber;
            ws.Cell(row, 2).Value = r.FullName;
            ws.Cell(row, 3).Value = r.Phone;
            ws.Cell(row, 4).Value = r.Course?.CourseName ?? "";
            ws.Cell(row, 5).Value = r.RegistrationDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 6).Value = r.Status;
            ws.Cell(row, 7).Value = r.ProcessedBy ?? "";
            row++;
        }
        ws.Columns().AdjustToContents();
        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"registrations_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
