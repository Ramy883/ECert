using ECert.Data;
using ECert.Models;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class CategoriesManageController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly IWebHostEnvironment _env;
    public CategoriesManageController(ECertDbContext db, AuditLogService audit, IWebHostEnvironment env)
    { _db = db; _audit = audit; _env = env; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public async Task<IActionResult> Index(string? search)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var query = _db.Categories.Include(c => c.Courses).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.CategoryName.Contains(search));
        ViewBag.Search = search;
        var categories = await query.OrderBy(c => c.CategoryName).ToListAsync();
        return View(categories);
    }

    [HttpGet]
    public IActionResult Create()
    {
        if (!HasPermission("manage-courses")) return Forbid();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category category, IFormFile? icon)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        if (string.IsNullOrWhiteSpace(category.CategoryName))
        {
            TempData["Error"] = "اسم الفئة مطلوب.";
            return View(category);
        }

        if (icon != null && icon.Length > 0)
            category.IconUrl = await SaveIcon(icon);

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Category", category.CategoryId, null, category.CategoryName);
        TempData["Success"] = "تمت إضافة الفئة بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();
        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category category, IFormFile? icon)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var existing = await _db.Categories.FindAsync(id);
        if (existing == null) return NotFound();

        existing.CategoryName = category.CategoryName;
        existing.Description = category.Description;
        existing.IsActive = category.IsActive;

        if (icon != null && icon.Length > 0)
            existing.IconUrl = await SaveIcon(icon);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Category", id, null, category.CategoryName);
        TempData["Success"] = "تم تعديل الفئة بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var category = await _db.Categories.Include(c => c.Courses).FirstOrDefaultAsync(c => c.CategoryId == id);
        if (category == null) return NotFound();

        if (category.Courses.Any())
        {
            TempData["Error"] = "لا يمكن حذف فئة تحتوي على دورات.";
            return RedirectToAction("Index");
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "Category", id);
        TempData["Success"] = "تم حذف الفئة.";
        return RedirectToAction("Index");
    }

    private async Task<string> SaveIcon(IFormFile icon)
    {
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "categories");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(icon.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await icon.CopyToAsync(stream);
        return $"/uploads/categories/{fileName}";
    }
}
