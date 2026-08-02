using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class AuditLog
{
    [Key]
    public int AuditLogId { get; set; }

    [Display(Name = "المستخدم")]
    public string UserName { get; set; } = string.Empty;

    [Display(Name = "العملية")]
    public string Action { get; set; } = string.Empty;
    // Create, Update, Delete, Accept, Reject, Issue, Cancel, Login

    [Display(Name = "نوع العنصر")]
    public string EntityType { get; set; } = string.Empty;
    // Registration, Invoice, Payment, Certificate, Course, User, Post

    [Display(Name = "رقم العنصر")]
    public int? EntityId { get; set; }

    [Display(Name = "البيانات القديمة")]
    public string? OldValues { get; set; }

    [Display(Name = "البيانات الجديدة")]
    public string? NewValues { get; set; }

    [Display(Name = "التاريخ والوقت")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [Display(Name = "عنوان IP")]
    public string? IpAddress { get; set; }
}
