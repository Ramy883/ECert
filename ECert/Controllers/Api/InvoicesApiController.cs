using ECert.Data;
using ECert.Models;
using ECert.Models.ApiModels;
using ECert.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public InvoicesApiController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    [HttpGet]
    public async Task<IActionResult> Get(string? status)
    {
        if (!HasPermission("view-invoices"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var query = _db.Invoices.AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);

        var invoices = await query.OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.InvoiceId, i.InvoiceNumber, i.TraineeName, i.TraineePhone,
                i.CourseName, i.TotalAmount, i.PaidAmount,
                remainingAmount = i.TotalAmount - i.PaidAmount,
                i.Status, i.CreatedAt, i.DueDate
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(invoices));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!HasPermission("view-invoices"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var invoice = await _db.Invoices.Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);
        if (invoice == null) return NotFound(ApiResponse.Fail("الفاتورة غير موجودة"));

        return Ok(ApiResponse<object>.Ok(new
        {
            invoice.InvoiceId, invoice.InvoiceNumber, invoice.TraineeName,
            invoice.TraineePhone, invoice.CourseName, invoice.TotalAmount,
            invoice.PaidAmount, remainingAmount = invoice.TotalAmount - invoice.PaidAmount,
            invoice.Status, invoice.CreatedAt, invoice.DueDate, invoice.CreatedBy,
            payments = invoice.Payments.Select(p => new
            {
                p.PaymentId, p.Amount, p.PaymentMethod, p.PaymentDate, p.Notes, p.RecordedBy, p.IsCancelled
            })
        }));
    }

    public record CreateInvoiceRequest(int RegistrationId, DateTime? DueDate);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest req)
    {
        if (!HasPermission("manage-finance"))
            return Ok(ApiResponse.Fail("ليس لديك صلاحية الوصول"));

        var reg = await _db.Registrations.Include(r => r.Course)
            .FirstOrDefaultAsync(r => r.RegistrationId == req.RegistrationId);
        if (reg == null) return Ok(ApiResponse.Fail("التسجيل غير موجود"));

        var existing = await _db.Invoices.AnyAsync(i => i.RegistrationId == req.RegistrationId);
        if (existing) return Ok(ApiResponse.Fail("توجد فاتورة لهذا التسجيل بالفعل"));

        var invoiceNumber = $"INV-{DateTime.Now:yyyy}-{new Random().Next(10000, 99999)}";
        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            RegistrationId = req.RegistrationId,
            TraineeName = reg.FullName,
            TraineePhone = reg.Phone,
            CourseName = reg.Course?.CourseName ?? "",
            TotalAmount = reg.Course?.Price ?? 0,
            Status = "Unpaid",
            CreatedAt = DateTime.Now,
            DueDate = req.DueDate,
            CreatedBy = User.Identity?.Name ?? "System"
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Invoice", invoice.InvoiceId, null, invoiceNumber);

        return Ok(ApiResponse<object>.Ok(new { id = invoice.InvoiceId, invoiceNumber }, "تم إنشاء الفاتورة بنجاح"));
    }
}
