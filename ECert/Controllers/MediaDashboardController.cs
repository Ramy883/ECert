using ECert.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize(Roles = "Media,SuperAdmin")]
public class MediaDashboardController : Controller
{
    private readonly ECertDbContext _db;
    public MediaDashboardController(ECertDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.TotalPosts = await _db.Posts.CountAsync(p => p.PostType == "Post");
        ViewBag.PublishedPosts = await _db.Posts.CountAsync(p => p.PostType == "Post" && p.Status == "Published");
        ViewBag.DraftPosts = await _db.Posts.CountAsync(p => p.PostType == "Post" && p.Status == "Draft");
        ViewBag.TotalNews = await _db.Posts.CountAsync(p => p.PostType == "News");
        ViewBag.PublishedNews = await _db.Posts.CountAsync(p => p.PostType == "News" && p.Status == "Published");
        ViewBag.RecentPosts = await _db.Posts.OrderByDescending(p => p.CreatedAt).Take(5).ToListAsync();
        return View();
    }
}
