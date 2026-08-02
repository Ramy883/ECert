using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class PhoneCountry
{
    [Key]
    public int PhoneCountryId { get; set; }

    [Required(ErrorMessage = "اسم الدولة مطلوب")]
    [Display(Name = "اسم الدولة")]
    public string CountryName { get; set; } = string.Empty;

    [Required(ErrorMessage = "مفتاح الدولة مطلوب")]
    [Display(Name = "مفتاح الدولة")]
    public string CountryCode { get; set; } = string.Empty; // e.g. +967

    [Display(Name = "الحد الأدنى لطول الرقم")]
    public int MinPhoneLength { get; set; } = 9;

    [Display(Name = "الحد الأقصى لطول الرقم")]
    public int MaxPhoneLength { get; set; } = 9;

    [Display(Name = "البدايات المسموحة")]
    public string? Prefixes { get; set; } // comma-separated: 70,71,73,77,78

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;
}
