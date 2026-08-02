using ECert.Data;
using ECert.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

public class HomeController : Controller
{
    private readonly ECertDbContext _db;
    public HomeController(ECertDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        // CMS Data
        ViewBag.Slides = await _db.HeroSlides.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync();
        ViewBag.AnimatedTexts = await _db.HeroAnimatedTexts.Where(t => t.IsActive).OrderBy(t => t.SortOrder).Select(t => t.Text).ToListAsync();
        ViewBag.Sections = await _db.HomepageSections.Where(s => s.IsVisible).OrderBy(s => s.SortOrder).ToListAsync();
        ViewBag.StatCards = await _db.StatCards.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync();
        ViewBag.SocialLinks = await _db.SocialLinks.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync();
        ViewBag.ContactInfo = await _db.ContactInfos.FirstOrDefaultAsync();
        ViewBag.Theme = await _db.ThemeSettings.FirstOrDefaultAsync() ?? new ThemeSetting();
        ViewBag.SiteSettings = await _db.SiteSettings.ToListAsync();

        // Categories with course counts
        ViewBag.Categories = await _db.Categories
            .Where(c => c.IsActive)
            .Select(c => new { c.CategoryId, c.CategoryName, c.IconUrl, c.Description, CourseCount = c.Courses.Count(x => x.Status == "OpenForRegistration" || x.Status == "Published") })
            .ToListAsync();

        // Latest courses
        ViewBag.LatestCourses = await _db.Courses
            .Include(c => c.Instructor).Include(c => c.Category)
            .Where(c => c.Status == "OpenForRegistration" || c.Status == "Published")
            .OrderByDescending(c => c.CreatedAt).Take(6).ToListAsync();

        // Latest news
        ViewBag.LatestNews = await _db.Posts
            .Where(p => p.Status == "Published" && p.PostType == "News")
            .OrderByDescending(p => p.PublishedAt).Take(3).ToListAsync();

        // Instructors
        ViewBag.Instructors = await _db.Instructors
            .Where(i => i.IsActive)
            .Take(4).ToListAsync();

        // Dynamic Statistics
        ViewBag.StatsCourses = await _db.Courses.CountAsync(c => c.Status != "Archived" && c.Status != "Draft");
        ViewBag.StatsInstructors = await _db.Instructors.CountAsync(i => i.IsActive);
        ViewBag.StatsTrainees = await _db.Registrations.CountAsync();
        ViewBag.StatsCertificates = await _db.Certificates.CountAsync();

        return View();
    }

    public IActionResult About() => View();
    public IActionResult Contact() => View();
    public IActionResult AccessDenied() => View("AccessDenied");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
