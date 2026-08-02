using ECert.Data;
using ECert.Models;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/instructors")]
[Authorize]
public class InstructorsApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly IWebHostEnvironment _env;
    public InstructorsApiController(ECertDbContext db, AuditLogService audit, IWebHostEnvironment env)
    { _db = db; _audit = audit; _env = env; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    private async Task<string?> SavePhoto(IFormFile? photo)
    {
        if (photo == null || photo.Length == 0) return null;
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "instructors");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(photo.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await photo.CopyToAsync(stream);
        return $"/uploads/instructors/{fileName}";
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!HasPermission("manage-instructors"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var instructors = await _db.Instructors.Include(i => i.Courses)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.InstructorId, i.FullName, i.PhotoUrl, i.Specialization,
                i.Phone, i.Email, i.IsActive, i.CreatedAt,
                coursesCount = i.Courses.Count
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(instructors));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] string FullName, [FromForm] string? Bio,
        [FromForm] string? Specialization, [FromForm] string? Phone, [FromForm] string? Email, IFormFile? photo)
    {
        if (!HasPermission("manage-instructors"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        if (string.IsNullOrWhiteSpace(FullName))
            return Ok(ApiResponse.Fail("اسم المدرب مطلوب"));

        var instructor = new Instructor
        {
            FullName = FullName,
            Bio = Bio,
            Specialization = Specialization,
            Phone = Phone,
            Email = Email,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        var photoPath = await SavePhoto(photo);
        if (photoPath != null) instructor.PhotoUrl = photoPath;

        _db.Instructors.Add(instructor);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Instructor", instructor.InstructorId, null, FullName);

        return Ok(ApiResponse<object>.Ok(new { id = instructor.InstructorId }, "تمت إضافة المدرب بنجاح"));
    }

    [HttpPut("{id}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(int id, [FromForm] string FullName, [FromForm] string? Bio,
        [FromForm] string? Specialization, [FromForm] string? Phone, [FromForm] string? Email,
        [FromForm] bool IsActive, IFormFile? photo)
    {
        if (!HasPermission("manage-instructors"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var instructor = await _db.Instructors.FindAsync(id);
        if (instructor == null) return NotFound(ApiResponse.Fail("المدرب غير موجود"));

        instructor.FullName = FullName;
        instructor.Bio = Bio;
        instructor.Specialization = Specialization;
        instructor.Phone = Phone;
        instructor.Email = Email;
        instructor.IsActive = IsActive;

        var photoPath = await SavePhoto(photo);
        if (photoPath != null) instructor.PhotoUrl = photoPath;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Instructor", id, null, FullName);

        return Ok(ApiResponse.Ok("تم تعديل بيانات المدرب بنجاح"));
    }
}
