using ECert.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize(Roles = "Admin,SuperAdmin")]
public class ManagerDashboardController : Controller
{
    private readonly ECertDbContext _db;
    public ManagerDashboardController(ECertDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.PendingRegistrations = await _db.Registrations.CountAsync(r => r.Status == "Pending");
        ViewBag.TotalTrainees = await _db.Registrations.CountAsync(r => r.Status == "Accepted");
        ViewBag.ActiveCourses = await _db.Courses.CountAsync(c => c.Status == "OpenForRegistration" || c.Status == "InProgress");
        ViewBag.TotalCourses = await _db.Courses.CountAsync(c => c.Status != "Archived");
        ViewBag.TotalInstructors = await _db.Instructors.CountAsync(i => i.IsActive);
        ViewBag.CertificatesIssued = await _db.Certificates.CountAsync();
        ViewBag.RecentRegistrations = await _db.Registrations.Include(r => r.Course).OrderByDescending(r => r.RegistrationDate).Take(5).ToListAsync();
        return View();
    }
}
