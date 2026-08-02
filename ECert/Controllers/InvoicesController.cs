using ECert.Data;
using ECert.Models;
using ECert.Models.ViewModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class InvoicesController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public InvoicesController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public async Task<IActionResult> Index(string? status, string? search)
    {
        if (!HasPermission("view-invoices")) return Forbid();
        var query = _db.Invoices.Include(i => i.Payments).AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(i => i.InvoiceNumber.Contains(search) || i.TraineeName.Contains(search) || i.TraineePhone.Contains(search) || i.CourseName.Contains(search));
        }
        ViewBag.Invoices = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
        ViewBag.Status = status;
        ViewBag.Search = search;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Create(int registrationId)
    {
        if (!HasPermission("manage-finance")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == registrationId);
        if (reg == null) return NotFound();
        if (reg.Invoice != null) { TempData["Error"] = "توجد فاتورة already لهذا التسجيل"; return RedirectToAction("Index"); }

        ViewBag.Registration = reg;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int registrationId, DateTime? dueDate)
    {
        if (!HasPermission("manage-finance")) return Forbid();
        var reg = await _db.Registrations.Include(r => r.Course).FirstOrDefaultAsync(r => r.RegistrationId == registrationId);
        if (reg == null) return NotFound();

        var invoiceNumber = $"INV-{DateTime.Now:yyyy}-{new Random().Next(10000, 99999)}";
        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            RegistrationId = registrationId,
            TraineeName = reg.FullName,
            TraineePhone = reg.Phone,
            CourseName = reg.Course?.CourseName ?? "",
            TotalAmount = reg.Course?.FinalPrice ?? 0,
            Status = "Unpaid",
            CreatedAt = DateTime.Now,
            DueDate = dueDate,
            CreatedBy = User.Identity?.Name ?? "System"
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Invoice", invoice.InvoiceId, null, $"Invoice: {invoiceNumber}, Amount: {invoice.TotalAmount}");

        TempData["Success"] = $"تم إنشاء الفاتورة {invoiceNumber} بنجاح.";
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!HasPermission("view-invoices")) return Forbid();
        var invoice = await _db.Invoices.Include(i => i.Payments).Include(i => i.Registration).ThenInclude(r => r!.Course).FirstOrDefaultAsync(i => i.InvoiceId == id);
        if (invoice == null) return NotFound();
        return View(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!HasPermission("manage-finance")) return Forbid();
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null) return NotFound();
        invoice.Status = "Cancelled";
        invoice.CancelledBy = User.Identity?.Name ?? "System";
        invoice.CancelledAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Cancel", "Invoice", id);
        TempData["Success"] = "تم إلغاء الفاتورة.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        if (!HasPermission("manage-finance")) return Forbid();
        var invoice = await _db.Invoices.FindAsync(id);
        if (invoice == null) return NotFound();
        // Restore to appropriate status based on paid amount
        if (invoice.PaidAmount >= invoice.TotalAmount)
            invoice.Status = "Paid";
        else if (invoice.PaidAmount > 0)
            invoice.Status = "PartiallyPaid";
        else
            invoice.Status = "Unpaid";
        invoice.RestoredBy = User.Identity?.Name ?? "System";
        invoice.RestoredAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Restore", "Invoice", id);
        TempData["Success"] = "تم استعادة الفاتورة.";
        return RedirectToAction("Index");
    }
}
