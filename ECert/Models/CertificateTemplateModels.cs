using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class CertificateTemplateSettings
{
    [Key]
    public int CertificateTemplateSettingsId { get; set; }

    [Required, StringLength(160)]
    [Display(Name = "عنوان الشهادة")]
    public string CertificateTitle { get; set; } = "شهادة إتمام";

    [Required, StringLength(2000)]
    [Display(Name = "النص الرئيسي")]
    public string MainText { get; set; } = "تشهد الجهة بأن المتدرب قد أتم متطلبات الدورة التدريبية بنجاح.";

    [Required, StringLength(200)]
    [Display(Name = "اسم المركز")]
    public string CenterName { get; set; } = "مركز التدريب المهني";

    [StringLength(1000)]
    [Display(Name = "النصوص الإضافية")]
    public string? AdditionalText { get; set; }

    [StringLength(200)]
    [Display(Name = "نص التحقق")]
    public string VerificationLabel { get; set; } = "يمكن التحقق من صحة الشهادة عبر الرابط التالي";

    [StringLength(500)]
    [Display(Name = "مسار الشعار")]
    public string? LogoPath { get; set; }

    [StringLength(500)]
    [Display(Name = "مسار التوقيع الأيمن")]
    public string? RightSignaturePath { get; set; }

    [StringLength(500)]
    [Display(Name = "مسار التوقيع الأيسر")]
    public string? LeftSignaturePath { get; set; }

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
    [Display(Name = "اللون الرئيسي")]
    public string PrimaryColor { get; set; } = "#173B67";

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
    [Display(Name = "اللون المميز")]
    public string AccentColor { get; set; } = "#C9A227";

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
    [Display(Name = "لون النص")]
    public string TextColor { get; set; } = "#1F2937";

    [Display(Name = "عرض رمز التحقق")]
    public bool ShowVerificationCode { get; set; } = true;

    [Display(Name = "عرض رقم الشهادة")]
    public bool ShowCertificateNumber { get; set; } = true;

    [Display(Name = "عرض تاريخ الإصدار")]
    public bool ShowIssueDate { get; set; } = true;

    [Display(Name = "عرض رابط التحقق")]
    public bool ShowVerificationUrl { get; set; } = true;

    [Required]
    [Display(Name = "ترتيب العناصر")]
    public string ElementOrder { get; set; } = "logo,title,body,trainee,course,date,additional,verification,signatures";

    public int TemplateVersion { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public CertificateTemplateSettings Clone() => new()
    {
        CertificateTitle = CertificateTitle,
        MainText = MainText,
        CenterName = CenterName,
        AdditionalText = AdditionalText,
        VerificationLabel = VerificationLabel,
        LogoPath = LogoPath,
        RightSignaturePath = RightSignaturePath,
        LeftSignaturePath = LeftSignaturePath,
        PrimaryColor = PrimaryColor,
        AccentColor = AccentColor,
        TextColor = TextColor,
        ShowVerificationCode = ShowVerificationCode,
        ShowCertificateNumber = ShowCertificateNumber,
        ShowIssueDate = ShowIssueDate,
        ShowVerificationUrl = ShowVerificationUrl,
        ElementOrder = ElementOrder,
        TemplateVersion = TemplateVersion,
        UpdatedAt = UpdatedAt
    };
}

public class CertificatePrintViewModel
{
    public Certificate Certificate { get; set; } = new();
    public CertificateTemplateSettings Template { get; set; } = new();
    public string VerificationUrl { get; set; } = string.Empty;
    public bool IsLegacyTemplate { get; set; }
}
