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
    { _db = db; _audit = audit; _notify = notify; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public async Task<IActionResult> Index(string? status, string? search, int? courseId, DateTime? dateFrom, DateTime? dateTo)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var query = _db.Registrations.Include(r => r.Course).AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.FullName.Contains(search) || r.Phone.Contains(search) || r.RequestNumber.Contains(search));
        if (courseId.HasValue) query = query.Where(r => r.CourseId == courseId.Value);
        if (dateFrom.HasValue) query = query.Where(r => r.RegistrationDate >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(r => r.RegistrationDate <= dateTo.Value.AddDays(1));
        ViewBag.Registrations = await query.OrderByDescending(r => r.RegistrationDate).ToListAsync();
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.CourseId = courseId;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        ViewBag.Courses = await _db.Courses.Select(c => new { c.CourseId, c.CourseName }).ToListAsync();
        return View();
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).Include(r => r.Invoice).ThenInclude(i => i!.Payments).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();
        return View(reg);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();

        reg.Status = "Accepted";
        reg.AcceptedDate = DateTime.Now;
        reg.ProcessedBy = User.Identity?.Name ?? "System";
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Accept", "Registration", id, null, "Status: Accepted");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "RegistrationAccepted",
            $"عزيزي {reg.FullName}، تم قبولك في دورة {reg.Course?.CourseName}. سيتم التواصل معك قريباً.", "SMS", "Registration", id);

        TempData["Success"] = $"تم قبول طلب {reg.FullName} بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Reject(int id)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();
        return View(new RejectRegistrationViewModel { RegistrationId = id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(RejectRegistrationViewModel model)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == model.RegistrationId);
        if (reg == null) return NotFound();

        reg.Status = "Rejected";
        reg.RejectionReason = model.Reason;
        reg.RejectedDate = DateTime.Now;
        reg.ProcessedBy = User.Identity?.Name ?? "System";

        // Free up the seat
        var course = await _db.Courses.FindAsync(reg.CourseId);
        if (course != null && course.BookedSeats > 0) course.BookedSeats--;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Reject", "Registration", model.RegistrationId, null, $"Reason: {model.Reason}");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "RegistrationRejected",
            $"عزيزي {reg.FullName}، نأسف لإبلاغك بأن طلب تسجيلك في دورة {reg.Course?.CourseName} لم يتم قبوله. السبب: {model.Reason}", "SMS", "Registration", model.RegistrationId);

        TempData["Success"] = "تم رفض الطلب بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound();

        reg.Status = "Pending";
        reg.ReopenedDate = DateTime.Now;
        reg.RejectionReason = null;
        reg.RejectedDate = null;

        // Re-book the seat
        var course = await _db.Courses.FindAsync(reg.CourseId);
        if (course != null) course.BookedSeats++;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Reopen", "Registration", id, null, "Reopened to Pending");
        TempData["Success"] = "تم إعادة فتح الطلب بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var reg = await _db.Registrations.FindAsync(id);
        if (reg == null) return NotFound();

        reg.Status = "Archived";
        reg.ArchivedDate = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Archive", "Registration", id);
        TempData["Success"] = "تم أرشفة الطلب.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        // SuperAdmin only
        var isSuperAdmin = User.HasClaim(c => c.Type == "Role" && c.Value == "SuperAdmin");
        if (!isSuperAdmin) return Forbid();

        var reg = await _db.Registrations.FindAsync(id);
        if (reg == null) return NotFound();

        _db.Registrations.Remove(reg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "Registration", id, null, $"Permanently deleted: {reg.FullName}");
        TempData["Success"] = "تم حذف الطلب نهائياً.";
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> ExportXlsx(string? status, string? search, int? courseId, DateTime? dateFrom, DateTime? dateTo)
    {
        if (!HasPermission("manage-registrations")) return Forbid();
        var query = _db.Registrations.Include(r => r.Course).AsQueryable();
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
