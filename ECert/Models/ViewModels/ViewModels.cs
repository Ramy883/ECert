using System.ComponentModel.DataAnnotations;

namespace ECert.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [Display(Name = "اسم المستخدم")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;
}

public class PublicRegistrationViewModel
{
    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "الدولة مطلوبة")]
    [Display(Name = "الدولة")]
    public int CountryId { get; set; }

    [Display(Name = "البريد الإلكتروني (اختياري)")]
    [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
    public string? Email { get; set; }

    [Display(Name = "كيف سمعت عنا؟ (اختياري)")]
    public string? HeardFrom { get; set; }

    public int CourseId { get; set; }
    public string? CourseName { get; set; }
}

public class DashboardViewModel
{
    public int PendingRegistrations { get; set; }
    public int UnpaidInvoices { get; set; }
    public int ActiveCourses { get; set; }
    public int NewPosts { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalTrainees { get; set; }
    public List<Registration> RecentRegistrations { get; set; } = new();
    public List<Invoice> RecentInvoices { get; set; } = new();
}

public class RejectRegistrationViewModel
{
    public int RegistrationId { get; set; }
    [Required(ErrorMessage = "سبب الرفض مطلوب")]
    [Display(Name = "سبب الرفض")]
    public string Reason { get; set; } = string.Empty;
}

public class AddPaymentViewModel
{
    public int InvoiceId { get; set; }
    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "طريقة الدفع مطلوبة")]
    [Display(Name = "طريقة الدفع")]
    public string PaymentMethod { get; set; } = "Cash";

    [Display(Name = "رقم السند / رقم العملية")]
    public string? ReferenceNumber { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [Display(Name = "اسم المستخدم")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress]
    [Display(Name = "البريد الإلكتروني")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "رقم الهاتف")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "الدور")]
    public int RoleId { get; set; }
}

public class ReportsViewModel
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining { get; set; }
    public decimal TotalDiscounts { get; set; }
    public int UnpaidCount { get; set; }
    public int PaidCount { get; set; }
    public List<Invoice> Invoices { get; set; } = new();
    public string Period { get; set; } = "month";
}
