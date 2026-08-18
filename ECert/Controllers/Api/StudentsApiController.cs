using ECert.Data;
using ECert.Models;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/students")]
[Authorize]
public class StudentsApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public StudentsApiController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var students = await _db.Registrations
            .Where(r => r.Status == "Accepted")
            .Include(r => r.Course)
            .Select(r => new
            {
                r.RegistrationId, r.FullName, r.Phone, r.Email,
                courseName = r.Course!.CourseName, r.AcceptedDate
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(students));
    }

    public record AddStudentRequest(string FullName, string Phone, string? Email, int CourseId);

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddStudentRequest req)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var course = await _db.Courses.FindAsync(req.CourseId);
        if (course == null) return Ok(ApiResponse.Fail("الدورة غير موجودة"));

        var reg = new Registration
        {
            RequestNumber = $"REG-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
            FullName = req.FullName,
            Phone = req.Phone,
            Email = req.Email,
            CourseId = req.CourseId,
            Status = "Accepted",
            RegistrationDate = DateTime.Now,
            AcceptedDate = DateTime.Now,
            ProcessedBy = User.Identity?.Name ?? "System"
        };

        _db.Registrations.Add(reg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Student", reg.RegistrationId, null, req.FullName);

        return Ok(ApiResponse<object>.Ok(new { id = reg.RegistrationId }, "تمت إضافة الطالب بنجاح"));
    }

    public record UpdateStudentRequest(string FullName, string Phone, string? Email);

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateStudentRequest req)
    {
        if (!HasPermission("manage-registrations"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.FindAsync(id);
        if (reg == null) return NotFound(ApiResponse.Fail("الطالب غير موجود"));

        reg.FullName = req.FullName;
        reg.Phone = req.Phone;
        reg.Email = req.Email;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Student", id, null, req.FullName);

        return Ok(ApiResponse.Ok("تم تعديل بيانات الطالب بنجاح"));
    }
}
