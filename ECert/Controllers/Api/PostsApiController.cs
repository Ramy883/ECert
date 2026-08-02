using ECert.Data;
using ECert.Models;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/posts")]
[Authorize]
public class PostsApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public PostsApiController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!HasPermission("manage-posts"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var posts = await _db.Posts.Where(p => p.PostType == "Post")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.PostId, p.Title, p.Content, p.ImageUrl, p.Author,
                p.Status, p.PublishedAt, p.CreatedAt
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(posts));
    }

    public record CreatePostRequest(string Title, string Content, string? ImageUrl, string Status);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePostRequest req)
    {
        if (!HasPermission("manage-posts"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Content))
            return Ok(ApiResponse.Fail("العنوان والمحتوى مطلوبان"));

        var post = new Post
        {
            Title = req.Title,
            Content = req.Content,
            ImageUrl = req.ImageUrl,
            PostType = "Post",
            Author = User.Identity?.Name ?? "Unknown",
            Status = req.Status ?? "Draft",
            CreatedAt = DateTime.Now
        };
        if (post.Status == "Published") post.PublishedAt = DateTime.Now;

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Post", post.PostId, null, req.Title);

        return Ok(ApiResponse<object>.Ok(new { id = post.PostId }, "تم إنشاء المنشور بنجاح"));
    }

    public record UpdatePostRequest(string Title, string Content, string? ImageUrl, string Status);

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePostRequest req)
    {
        if (!HasPermission("manage-posts"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var post = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.PostType == "Post");
        if (post == null) return NotFound(ApiResponse.Fail("المنشور غير موجود"));

        post.Title = req.Title;
        post.Content = req.Content;
        post.ImageUrl = req.ImageUrl;
        post.Status = req.Status;
        post.UpdatedAt = DateTime.Now;
        if (req.Status == "Published" && post.PublishedAt == null) post.PublishedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Post", id, null, req.Title);

        return Ok(ApiResponse.Ok("تم تعديل المنشور بنجاح"));
    }

    [HttpPut("{id}/publish")]
    public async Task<IActionResult> Publish(int id)
    {
        if (!HasPermission("manage-posts"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var post = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.PostType == "Post");
        if (post == null) return NotFound(ApiResponse.Fail("المنشور غير موجود"));

        post.Status = "Published";
        post.PublishedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse.Ok("تم نشر المنشور"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!HasPermission("manage-posts"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var post = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.PostType == "Post");
        if (post == null) return NotFound(ApiResponse.Fail("المنشور غير موجود"));

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "Post", id, null, post.Title);

        return Ok(ApiResponse.Ok("تم حذف المنشور"));
    }
}
