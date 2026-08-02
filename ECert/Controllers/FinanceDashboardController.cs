using ECert.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize(Roles = "Finance,SuperAdmin")]
public class FinanceDashboardController : Controller
{
    private readonly ECertDbContext _db;
    public FinanceDashboardController(ECertDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var invoices = await _db.Invoices.ToListAsync();
        var payments = await _db.Payments.Where(p => !p.IsCancelled).ToListAsync();

        ViewBag.TotalRevenue = invoices.Sum(i => i.TotalAmount);
        ViewBag.TotalPaid = payments.Sum(p => p.Amount);
        ViewBag.TotalRemaining = invoices.Where(i => i.Status != "Cancelled").Sum(i => i.RemainingAmount);
        ViewBag.UnpaidCount = invoices.Count(i => i.Status == "Unpaid");
        ViewBag.PaidCount = invoices.Count(i => i.Status == "Paid");
        ViewBag.PartialCount = invoices.Count(i => i.Status == "PartiallyPaid");
        ViewBag.RecentPayments = await _db.Payments.Include(p => p.Invoice).Where(p => !p.IsCancelled).OrderByDescending(p => p.PaymentDate).Take(5).ToListAsync();
        ViewBag.RecentInvoices = await _db.Invoices.OrderByDescending(i => i.CreatedAt).Take(5).ToListAsync();
        return View();
    }
}
