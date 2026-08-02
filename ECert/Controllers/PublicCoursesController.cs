using ECert.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

public class PublicCoursesController : Controller
{
    private readonly ECertDbContext _db;
    public PublicCoursesController(ECertDbContext db) => _db = db;

    public async Task<IActionResult> Index(int? categoryId, string? search)
    {
        ViewBag.Categories = await _db.Categories
            .Where(c => c.IsActive)
            .Include(c => c.Courses.Where(co => co.Status == "OpenForRegistration" || co.Status == "Published" || co.Status == "Full"))
            .ToListAsync();

        ViewBag.CurrentCategoryId = categoryId;
        ViewBag.Search = search;

        if (categoryId.HasValue || !string.IsNullOrEmpty(search))
        {
            var query = _db.Courses.Include(c => c.Instructor).Include(c => c.Category)
                .Where(c => c.Status == "OpenForRegistration" || c.Status == "Published" || c.Status == "Full");

            if (categoryId.HasValue)
                query = query.Where(c => c.CategoryId == categoryId.Value);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(c => c.CourseName.Contains(search));

            ViewBag.Courses = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        }

        return View();
    }

    public async Task<IActionResult> Details(int id)
    {
        var course = await _db.Courses.Include(c => c.Instructor).Include(c => c.Category)
            .FirstOrDefaultAsync(c => c.CourseId == id);
        if (course == null) return NotFound();
        return View(course);
    }
}
