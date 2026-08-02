using ECert.Data;
using ECert.Models;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/news")]
[Authorize]
public class NewsApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly IWebHostEnvironment _env;
    public NewsApiController(ECertDbContext db, AuditLogService audit, IWebHostEnvironment env)
    { _db = db; _audit = audit; _env = env; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    private async Task<string?> SaveImage(IFormFile? image)
    {
        if (image == null || image.Length == 0) return null;
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "news");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await image.CopyToAsync(stream);
        return $"/uploads/news/{fileName}";
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!HasPermission("manage-news"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var news = await _db.Posts.Where(p => p.PostType == "News")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.PostId, p.Title, p.Content, p.ImageUrl, p.Author,
                p.Status, p.PublishedAt, p.CreatedAt
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(news));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] string Title, [FromForm] string Content,
        [FromForm] string Status, IFormFile? image)
    {
        if (!HasPermission("manage-news"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Content))
            return Ok(ApiResponse.Fail("العنوان والمحتوى مطلوبان"));

        var news = new Post
        {
            Title = Title,
            Content = Content,
            PostType = "News",
            Author = User.Identity?.Name ?? "",
            Status = Status ?? "Draft",
            CreatedAt = DateTime.Now
        };
        if (news.Status == "Published") news.PublishedAt = DateTime.Now;

        var imgPath = await SaveImage(image);
        if (imgPath != null) news.ImageUrl = imgPath;

        _db.Posts.Add(news);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "News", news.PostId);

        return Ok(ApiResponse<object>.Ok(new { id = news.PostId }, "تمت إضافة الخبر بنجاح"));
    }

    [HttpPut("{id}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(int id, [FromForm] string Title, [FromForm] string Content,
        [FromForm] string Status, IFormFile? image)
    {
        if (!HasPermission("manage-news"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var news = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.PostType == "News");
        if (news == null) return NotFound(ApiResponse.Fail("الخبر غير موجود"));

        news.Title = Title;
        news.Content = Content;
        news.Status = Status;
        news.UpdatedAt = DateTime.Now;

        var imgPath = await SaveImage(image);
        if (imgPath != null) news.ImageUrl = imgPath;
        if (Status == "Published" && news.PublishedAt == null) news.PublishedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Edit", "News", news.PostId);

        return Ok(ApiResponse.Ok("تم تعديل الخبر بنجاح"));
    }

    [HttpPut("{id}/publish")]
    public async Task<IActionResult> Publish(int id)
    {
        if (!HasPermission("manage-news"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var news = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.PostType == "News");
        if (news == null) return NotFound(ApiResponse.Fail("الخبر غير موجود"));

        news.Status = "Published";
        news.PublishedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse.Ok("تم نشر الخبر"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!HasPermission("manage-news"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var news = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.PostType == "News");
        if (news == null) return NotFound(ApiResponse.Fail("الخبر غير موجود"));

        _db.Posts.Remove(news);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "News", id);

        return Ok(ApiResponse.Ok("تم حذف الخبر"));
    }
}
