using ECert.Data;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/registrations")]
[Authorize]
public class RegistrationsApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly NotificationService _notify;
    public RegistrationsApiController(ECertDbContext db, AuditLogService audit, NotificationService notify)
    { _db = db; _audit = audit; _notify = notify; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    [HttpGet]
    public async Task<IActionResult> Get(string? status)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var query = _db.Registrations.Include(r => r.Course).AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
        var list = await query.OrderByDescending(r => r.RegistrationDate)
            .Select(r => new
            {
                r.RegistrationId, r.RequestNumber, r.FullName, r.FullNameArabic, r.FullNameEnglish, r.Gender, r.Phone, r.Email,
                courseName = r.Course!.CourseName, r.Status, r.RegistrationDate, r.ProcessedBy
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.Include(r => r.Course)
            .FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound(ApiResponse.Fail("التسجيل غير موجود"));

        return Ok(ApiResponse<object>.Ok(new
        {
            reg.RegistrationId, reg.RequestNumber, reg.FullName, reg.FullNameArabic, reg.FullNameEnglish, reg.Gender, reg.Phone, reg.Email,
            reg.HeardFrom, courseName = reg.Course?.CourseName, reg.Status,
            reg.RegistrationDate, reg.AcceptedDate, reg.ProcessedBy, reg.RejectionReason
        }));
    }

    [HttpPost("accept/{id}")]
    public async Task<IActionResult> Accept(int id)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound(ApiResponse.Fail("التسجيل غير موجود"));

        reg.Status = "Accepted";
        reg.AcceptedDate = DateTime.Now;
        reg.ProcessedBy = User.Identity?.Name ?? "System";
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Accept", "Registration", id, null, "Status: Accepted");
        await _notify.SendAsync(reg.FullName, reg.Phone, reg.Email, "RegistrationAccepted",
            $"عزيزي {reg.FullName}، تم قبولك في دورة {reg.Course?.CourseName}.", "SMS", "Registration", id);

        return Ok(ApiResponse.Ok($"تم قبول طلب {reg.FullName} بنجاح"));
    }

    public record RejectRequest(string Reason);

    [HttpPost("reject/{id}")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectRequest req)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound(ApiResponse.Fail("التسجيل غير موجود"));

        reg.Status = "Rejected";
        reg.RejectionReason = req.Reason;
        reg.RejectedDate = DateTime.Now;
        reg.ProcessedBy = User.Identity?.Name ?? "System";

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Reject", "Registration", id, null, $"Reason: {req.Reason}");

        return Ok(ApiResponse.Ok("تم رفض الطلب بنجاح"));
    }

    [HttpPost("reopen/{id}")]
    public async Task<IActionResult> Reopen(int id)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == id);
        if (reg == null) return NotFound(ApiResponse.Fail("التسجيل غير موجود"));

        reg.Status = "Pending";
        reg.ReopenedDate = DateTime.Now;
        reg.RejectionReason = null;
        reg.RejectedDate = null;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Reopen", "Registration", id);
        return Ok(ApiResponse.Ok("تم إعادة فتح الطلب"));
    }

    [HttpPost("archive/{id}")]
    public async Task<IActionResult> Archive(int id)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.FindAsync(id);
        if (reg == null) return NotFound(ApiResponse.Fail("التسجيل غير موجود"));

        reg.Status = "Archived";
        reg.ArchivedDate = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Archive", "Registration", id);
        return Ok(ApiResponse.Ok("تم أرشفة الطلب"));
    }
}
