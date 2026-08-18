using ECert.Data;
using ECert.Models;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/courses")]
[Authorize]
public class CoursesApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public CoursesApiController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!HasPermission("manage-courses"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var courses = await _db.Courses
            .Include(c => c.Category).Include(c => c.Instructor)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.CourseId, c.CourseName, c.ShortDescription, c.Price,
                c.Status, c.StartDate, c.EndDate, c.Location,
                categoryName = c.Category!.CategoryName,
                instructorName = c.Instructor!.FullName
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(courses));
    }

    public record CreateCourseRequest(
        string CourseName, string? ShortDescription, string? FullDescription,
        int CategoryId, int InstructorId, DateTime? StartDate, DateTime? EndDate,
        string? Location, decimal Price, string Status);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCourseRequest req)
    {
        if (!HasPermission("manage-courses"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        if (string.IsNullOrWhiteSpace(req.CourseName))
            return Ok(ApiResponse.Fail("اسم الدورة مطلوب"));

        var course = new Course
        {
            CourseName = req.CourseName,
            ShortDescription = req.ShortDescription,
            FullDescription = req.FullDescription,
            CategoryId = req.CategoryId,
            InstructorId = req.InstructorId,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            Location = req.Location,
            Price = req.Price,
            Status = req.Status ?? "Draft",
            CreatedAt = DateTime.Now
        };

        _db.Courses.Add(course);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Course", course.CourseId, null, req.CourseName);

        return Ok(ApiResponse<object>.Ok(new { id = course.CourseId }, "تمت إضافة الدورة بنجاح"));
    }

    public record UpdateCourseRequest(
        string CourseName, string? ShortDescription, string? FullDescription,
        int CategoryId, int InstructorId, DateTime? StartDate, DateTime? EndDate,
        string? Location, decimal Price, string Status);

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCourseRequest req)
    {
        if (!HasPermission("manage-courses"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound(ApiResponse.Fail("الدورة غير موجودة"));

        course.CourseName = req.CourseName;
        course.ShortDescription = req.ShortDescription;
        course.FullDescription = req.FullDescription;
        course.CategoryId = req.CategoryId;
        course.InstructorId = req.InstructorId;
        course.StartDate = req.StartDate;
        course.EndDate = req.EndDate;
        course.Location = req.Location;
        course.Price = req.Price;
        course.Status = req.Status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Course", id, null, req.CourseName);

        return Ok(ApiResponse.Ok("تم تعديل الدورة بنجاح"));
    }

    public record CourseStatusRequest(string Status);

    [HttpPut("{id}/status")]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] CourseStatusRequest req)
    {
        if (!HasPermission("manage-courses"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound(ApiResponse.Fail("الدورة غير موجودة"));

        course.Status = req.Status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Course", id, null, $"Status: {req.Status}");

        return Ok(ApiResponse.Ok("تم تغيير حالة الدورة"));
    }
}
