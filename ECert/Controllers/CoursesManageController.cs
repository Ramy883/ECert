using ECert.Data;
using ECert.Models;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class CoursesManageController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly IWebHostEnvironment _env;
    public CoursesManageController(ECertDbContext db, AuditLogService audit, IWebHostEnvironment env)
    { _db = db; _audit = audit; _env = env; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    private async Task<string?> SaveImage(IFormFile? image)
    {
        if (image == null || image.Length == 0) return null;
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "courses");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await image.CopyToAsync(stream);
        return $"/uploads/courses/{fileName}";
    }

    public async Task<IActionResult> Index(string? search)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var query = _db.Courses.Include(c => c.Category).Include(c => c.Instructor).Include(c => c.Registrations).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.CourseNameArabic.Contains(search) || c.CourseNameEnglish.Contains(search) || c.CourseName.Contains(search));
        ViewBag.Search = search;
        var courses = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return View(courses);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!HasPermission("manage-courses")) return Forbid();
        ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
        ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Course course, IFormFile? image)
    {
        if (!HasPermission("manage-courses")) return Forbid();

        course.CourseNameEnglish = course.CourseNameEnglish?.Trim() ?? string.Empty;
        course.CourseNameArabic = course.CourseNameArabic?.Trim() ?? string.Empty;
        course.CourseName = course.CourseNameArabic;

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
            return View(course);
        }

        course.CreatedAt = DateTime.Now;
        var imgPath = await SaveImage(image);
        if (imgPath != null) course.ImageUrl = imgPath;
        _db.Courses.Add(course);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Course", course.CourseId, null, $"{course.CourseNameEnglish} / {course.CourseNameArabic}");
        TempData["Success"] = "تمت إضافة الدورة بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();
        ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
        ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
        return View(course);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Course course, IFormFile? image)
    {
        if (!HasPermission("manage-courses")) return Forbid();

        course.CourseNameEnglish = course.CourseNameEnglish?.Trim() ?? string.Empty;
        course.CourseNameArabic = course.CourseNameArabic?.Trim() ?? string.Empty;
        course.CourseName = course.CourseNameArabic;

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
            return View(course);
        }

        var existing = await _db.Courses.FindAsync(course.CourseId);
        if (existing == null) return NotFound();

        existing.CourseName = course.CourseNameArabic;
        existing.CourseNameEnglish = course.CourseNameEnglish;
        existing.CourseNameArabic = course.CourseNameArabic;
        existing.ShortDescription = course.ShortDescription;
        existing.FullDescription = course.FullDescription;
        existing.Objectives = course.Objectives;
        existing.Content = course.Content;
        existing.CategoryId = course.CategoryId;
        existing.InstructorId = course.InstructorId;
        existing.StartDate = course.StartDate;
        existing.EndDate = course.EndDate;
        existing.Location = course.Location;
        existing.Price = course.Price;
        existing.DiscountType = course.DiscountType;
        existing.DiscountValue = course.DiscountValue;
        existing.TotalSeats = course.TotalSeats;
        existing.Status = course.Status;
        existing.IsFeatured = course.IsFeatured;
        existing.RequiresAcademicDetails = course.RequiresAcademicDetails;
        var imgPath = await SaveImage(image);
        if (imgPath != null)
        {
            // Delete old image if exists
            if (!string.IsNullOrEmpty(existing.ImageUrl))
            {
                var oldImgPath = Path.Combine(_env.WebRootPath, existing.ImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(oldImgPath))
                    System.IO.File.Delete(oldImgPath);
            }
            existing.ImageUrl = imgPath;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Course", course.CourseId, null, $"{course.CourseNameEnglish} / {course.CourseNameArabic}");
        TempData["Success"] = "تم تعديل الدورة بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string status)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();
        course.Status = status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Course", id, null, $"Status: {status}");
        TempData["Success"] = "تم تغيير حالة الدورة.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();

        // Delete image file from disk
        if (!string.IsNullOrEmpty(course.ImageUrl))
        {
            var imgPath = Path.Combine(_env.WebRootPath, course.ImageUrl.TrimStart('/'));
            if (System.IO.File.Exists(imgPath))
                System.IO.File.Delete(imgPath);
        }

        course.Status = "Archived";
        course.ImageUrl = null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "Course", id);
        TempData["Success"] = "تم أرشفة الدورة.";
        return RedirectToAction("Index");
    }
}
