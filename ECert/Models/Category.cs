using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class Category
{
    [Key]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "اسم الفئة مطلوب")]
    [StringLength(100)]
    [Display(Name = "اسم الفئة")]
    public string CategoryName { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "أيقونة الفئة")]
    public string? IconUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Course> Courses { get; set; } = new List<Course>();
}
