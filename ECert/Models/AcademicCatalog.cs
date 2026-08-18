using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class University
{
    [Key]
    public int UniversityId { get; set; }

    [Required(ErrorMessage = "اسم الجامعة مطلوب")]
    [StringLength(160)]
    [Display(Name = "الجامعة")]
    public string UniversityName { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public bool IsActive { get; set; } = true;

    public ICollection<College> Colleges { get; set; } = new List<College>();
}

public class College
{
    [Key]
    public int CollegeId { get; set; }

    [Required]
    [Display(Name = "الجامعة")]
    public int UniversityId { get; set; }
    public University? University { get; set; }

    [Required(ErrorMessage = "اسم الكلية مطلوب")]
    [StringLength(160)]
    [Display(Name = "الكلية")]
    public string CollegeName { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public bool IsActive { get; set; } = true;

    public ICollection<AcademicSpecialization> Specializations { get; set; } = new List<AcademicSpecialization>();
}

public class AcademicSpecialization
{
    [Key]
    public int AcademicSpecializationId { get; set; }

    [Required]
    [Display(Name = "الكلية")]
    public int CollegeId { get; set; }
    public College? College { get; set; }

    [Required(ErrorMessage = "اسم التخصص مطلوب")]
    [StringLength(160)]
    [Display(Name = "التخصص")]
    public string SpecializationName { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public bool IsActive { get; set; } = true;

    public ICollection<AcademicLevelOption> Levels { get; set; } = new List<AcademicLevelOption>();
}

public class AcademicLevelOption
{
    [Key]
    public int AcademicLevelOptionId { get; set; }

    [Required]
    public int AcademicSpecializationId { get; set; }
    public AcademicSpecialization? AcademicSpecialization { get; set; }

    [Required(ErrorMessage = "اسم المستوى مطلوب")]
    [StringLength(80)]
    [Display(Name = "المستوى الدراسي")]
    public string LevelName { get; set; } = string.Empty;

    [Display(Name = "الترتيب")]
    public int SortOrder { get; set; }

    [Display(Name = "الحالة")]
    public bool IsActive { get; set; } = true;
}

public static class AcademicLevelCatalog
{
    public static readonly IReadOnlyList<string> Levels = new[]
    {
        "السنة التحضيرية",
        "المستوى الأول",
        "المستوى الثاني",
        "المستوى الثالث",
        "المستوى الرابع",
        "المستوى الخامس",
        "المستوى السادس",
        "المستوى السابع",
        "المستوى الثامن",
        "دراسات عليا"
    };

    public static bool IsValid(string? level)
        => !string.IsNullOrWhiteSpace(level) && Levels.Contains(level.Trim(), StringComparer.Ordinal);
}
