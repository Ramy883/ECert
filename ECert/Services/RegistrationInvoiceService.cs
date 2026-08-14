using ECert.Data;
using ECert.Models;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class RegistrationInvoiceService
{
    private readonly ECertDbContext _db;

    public RegistrationInvoiceService(ECertDbContext db) => _db = db;

    public async Task<Invoice> EnsureForAcceptedAsync(Registration registration, string createdBy)
    {
        if (registration.Invoice != null)
            return registration.Invoice;

        var existing = await _db.Invoices.FirstOrDefaultAsync(i => i.RegistrationId == registration.RegistrationId);
        if (existing != null)
        {
            registration.Invoice = existing;
            return existing;
        }

        if (!string.Equals(registration.Status, "Accepted", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("لا يمكن إنشاء فاتورة قبل قبول التسجيل.");

        if (registration.Course == null)
            registration.Course = await _db.Courses.FirstOrDefaultAsync(c => c.CourseId == registration.CourseId);

        if (registration.Course == null)
            throw new InvalidOperationException("لا يمكن إنشاء الفاتورة لأن الدورة غير موجودة.");

        var totalAmount = registration.Course.FinalPrice;
        var invoice = new Invoice
        {
            InvoiceNumber = await GenerateInvoiceNumberAsync(),
            RegistrationId = registration.RegistrationId,
            Registration = registration,
            TraineeName = registration.FullNameArabic ?? registration.FullName,
            TraineeNameArabic = registration.FullNameArabic ?? registration.FullName,
            TraineeNameEnglish = registration.FullNameEnglish ?? registration.FullName,
            TraineePhone = registration.Phone,
            CourseName = registration.Course.CourseNameArabic ?? registration.Course.CourseName,
            CourseNameEnglish = registration.Course.CourseNameEnglish ?? registration.Course.CourseName,
            CourseNameArabic = registration.Course.CourseNameArabic ?? registration.Course.CourseName,
            TotalAmount = totalAmount,
            Status = totalAmount <= 0 ? "Paid" : "Unpaid",
            CreatedAt = DateTime.Now,
            CreatedBy = createdBy
        };

        _db.Invoices.Add(invoice);
        registration.Invoice = invoice;
        return invoice;
    }

    private async Task<string> GenerateInvoiceNumberAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"INV-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";
            if (!await _db.Invoices.AnyAsync(i => i.InvoiceNumber == candidate))
                return candidate;
        }

        return $"INV-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40];
    }
}
