using ECert.Data;
using ECert.Models;
using ECert.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

public class RegistrationController : Controller
{
    private readonly ECertDbContext _db;
    public RegistrationController(ECertDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Register(int courseId)
    {
        var course = await _db.Courses.FindAsync(courseId);
        if (course == null) return NotFound();
        if (course.Status != "OpenForRegistration")
        {
            TempData["Error"] = "التسجيل غير متاح لهذه الدورة حالياً.";
            return RedirectToAction("Details", "PublicCourses", new { id = courseId });
        }
        if (course.AvailableSeats <= 0)
        {
            TempData["Error"] = "عذراً، العدد مكتمل في هذه الدورة.";
            return RedirectToAction("Details", "PublicCourses", new { id = courseId });
        }
        var vm = new PublicRegistrationViewModel { CourseId = courseId, CourseName = course.CourseName };
        ViewBag.Countries = await _db.PhoneCountries.Where(c => c.IsActive).OrderBy(c => c.CountryName).ToListAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(PublicRegistrationViewModel model)
    {
        var course = await _db.Courses.FindAsync(model.CourseId);
        if (course == null) return NotFound();

        ViewBag.Countries = await _db.PhoneCountries.Where(c => c.IsActive).OrderBy(c => c.CountryName).ToListAsync();

        if (!ModelState.IsValid)
        {
            model.CourseName = course.CourseName;
            return View(model);
        }

        // Validate phone based on selected country
        var country = await _db.PhoneCountries.FindAsync(model.CountryId);
        if (country == null || !country.IsActive)
        {
            ModelState.AddModelError("CountryId", "الدولة المختارة غير صحيحة");
            model.CourseName = course.CourseName;
            return View(model);
        }

        var phone = model.Phone.Trim();
        // Remove leading zero if present
        if (phone.StartsWith("0")) phone = phone[1..];

        // Validate length
        if (phone.Length < country.MinPhoneLength || phone.Length > country.MaxPhoneLength)
        {
            ModelState.AddModelError("Phone", $"رقم الهاتف يجب أن يكون بين {country.MinPhoneLength} و {country.MaxPhoneLength} أرقام");
            model.CourseName = course.CourseName;
            return View(model);
        }

        // Validate digits only
        if (!phone.All(char.IsDigit))
        {
            ModelState.AddModelError("Phone", "رقم الهاتف يجب أن يحتوي على أرقام فقط");
            model.CourseName = course.CourseName;
            return View(model);
        }

        // Validate prefixes
        if (!string.IsNullOrEmpty(country.Prefixes))
        {
            var allowedPrefixes = country.Prefixes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (allowedPrefixes.Length > 0 && !allowedPrefixes.Any(p => phone.StartsWith(p)))
            {
                ModelState.AddModelError("Phone", $"رقم الهاتف يجب أن يبدأ بأحد البادئات: {string.Join(", ", allowedPrefixes)}");
                model.CourseName = course.CourseName;
                return View(model);
            }
        }

        if (course.AvailableSeats <= 0)
        {
            ModelState.AddModelError("", "عذراً، لا توجد مقاعد متاحة في هذه الدورة.");
            model.CourseName = course.CourseName;
            return View(model);
        }

        var requestNumber = $"REG-{DateTime.Now:yyyy}-{new Random().Next(10000, 99999)}";

        var registration = new Registration
        {
            RequestNumber = requestNumber,
            FullName = model.FullName,
            Phone = $"{country.CountryCode}{phone}",
            Email = model.Email,
            HeardFrom = model.HeardFrom,
            CourseId = model.CourseId,
            Status = "Pending",
            RegistrationDate = DateTime.Now
        };

        _db.Registrations.Add(registration);
        course.BookedSeats++;
        if (course.AvailableSeats <= 0)
        {
            course.Status = "Full";
        }
        await _db.SaveChangesAsync();

        ViewBag.RequestNumber = requestNumber;
        ViewBag.FullName = model.FullName;
        ViewBag.CourseName = course.CourseName;
        return View("Success");
    }
}
