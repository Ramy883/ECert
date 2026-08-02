using ECert.Data;
using ECert.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace ECert.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ECertDbContext _db;
    public ReportsController(ECertDbContext db) => _db = db;

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public IActionResult Index() => RedirectToAction("CourseRevenue");

    public async Task<IActionResult> CourseRevenue(int? courseId)
    {
        if (!HasPermission("view-reports")) return Forbid();

        ViewBag.Courses = await _db.Courses.OrderBy(c => c.CourseName).Select(c => new { c.CourseId, c.CourseName }).ToListAsync();
        ViewBag.CourseId = courseId;

        var query = _db.Courses.Include(c => c.Registrations).ThenInclude(r => r.Invoice).ThenInclude(i => i!.Payments).AsQueryable();
        if (courseId.HasValue)
            query = query.Where(c => c.CourseId == courseId.Value);

        var courses = await query.ToListAsync();

        var data = courses.Select(c =>
        {
            var invoices = c.Registrations.Where(r => r.Invoice != null).Select(r => r.Invoice!).ToList();
            var payments = invoices.SelectMany(i => i.Payments).Where(p => !p.IsCancelled).ToList();
            var expected = invoices.Sum(i => i.TotalAmount);
            var paid = payments.Sum(p => p.Amount);
            var cash = payments.Where(p => p.PaymentMethod == "Cash").Sum(p => p.Amount);
            var bank = payments.Where(p => p.PaymentMethod != "Cash").Sum(p => p.Amount);
            return new
            {
                c.CourseName,
                Registrations = c.Registrations.Count(r => r.Status == "Accepted"),
                Expected = expected,
                Paid = paid,
                Remaining = expected - paid,
                FullPaid = invoices.Count(i => i.Status == "Paid"),
                Partial = invoices.Count(i => i.Status == "PartiallyPaid"),
                Unpaid = invoices.Count(i => i.Status == "Unpaid"),
                Cash = cash,
                Bank = bank
            };
        }).ToList();

        ViewBag.Data = data;
        return View();
    }

    public async Task<IActionResult> ExportCourseRevenueXlsx()
    {
        if (!HasPermission("view-reports")) return Forbid();

        var courses = await _db.Courses.Include(c => c.Registrations).ThenInclude(r => r.Invoice).ThenInclude(i => i!.Payments).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("إيرادات الدورات");
        ws.Cell(1, 1).Value = "الدورة";
        ws.Cell(1, 2).Value = "عدد المسجلين";
        ws.Cell(1, 3).Value = "الإيراد المتوقع";
        ws.Cell(1, 4).Value = "المدفوع";
        ws.Cell(1, 5).Value = "المتبقي";
        ws.Cell(1, 6).Value = "سداد كامل";
        ws.Cell(1, 7).Value = "جزئي";
        ws.Cell(1, 8).Value = "غير مسدد";
        ws.Cell(1, 9).Value = "نقدي";
        ws.Cell(1, 10).Value = "محافظ إلكترونية";
        int row = 2;
        foreach (var c in courses)
        {
            var invoices = c.Registrations.Where(r => r.Invoice != null).Select(r => r.Invoice!).ToList();
            var payments = invoices.SelectMany(i => i.Payments).Where(p => !p.IsCancelled).ToList();
            ws.Cell(row, 1).Value = c.CourseName;
            ws.Cell(row, 2).Value = c.Registrations.Count(r => r.Status == "Accepted");
            ws.Cell(row, 3).Value = (double)invoices.Sum(i => i.TotalAmount);
            ws.Cell(row, 4).Value = (double)payments.Sum(p => p.Amount);
            ws.Cell(row, 5).Value = (double)(invoices.Sum(i => i.TotalAmount) - payments.Sum(p => p.Amount));
            ws.Cell(row, 6).Value = invoices.Count(i => i.Status == "Paid");
            ws.Cell(row, 7).Value = invoices.Count(i => i.Status == "PartiallyPaid");
            ws.Cell(row, 8).Value = invoices.Count(i => i.Status == "Unpaid");
            ws.Cell(row, 9).Value = (double)payments.Where(p => p.PaymentMethod == "Cash").Sum(p => p.Amount);
            ws.Cell(row, 10).Value = (double)payments.Where(p => p.PaymentMethod != "Cash").Sum(p => p.Amount);
            row++;
        }
        ws.Columns().AdjustToContents();
        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"course_revenue_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
