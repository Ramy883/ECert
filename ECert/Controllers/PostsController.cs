using ECert.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

public class PostsController : Controller
{
    private readonly ECertDbContext _db;
    public PostsController(ECertDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var posts = await _db.Posts.Where(p => p.Status == "Published")
            .OrderByDescending(p => p.PublishedAt).ToListAsync();
        return View(posts);
    }

    public async Task<IActionResult> Details(int id)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.PostId == id && p.Status == "Published");
        if (post == null) return NotFound();
        return View(post);
    }
}
