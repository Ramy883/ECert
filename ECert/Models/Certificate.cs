using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class Certificate
{
    [Key]
    public int CertificateId { get; set; }

    [Display(Name = "رقم الشهادة")]
    public string CertificateNumber { get; set; } = string.Empty;

    [Required]
    public int RegistrationId { get; set; }
    public Registration? Registration { get; set; }

    [Display(Name = "اسم المتدرب")]
    public string TraineeName { get; set; } = string.Empty;

    [Display(Name = "اسم الدورة")]
    public string CourseName { get; set; } = string.Empty;

    [Display(Name = "تاريخ الإصدار")]
    public DateTime IssueDate { get; set; } = DateTime.Now;

    [Display(Name = "أصدرها")]
    public string IssuedBy { get; set; } = string.Empty;

    [Display(Name = "رمز التحقق")]
    public string VerificationCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
