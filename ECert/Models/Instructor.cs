using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class Instructor
{
    [Key]
    public int InstructorId { get; set; }

    [Required(ErrorMessage = "اسم المدرب مطلوب")]
    [StringLength(100)]
    [Display(Name = "اسم المدرب")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "الصورة")]
    public string? PhotoUrl { get; set; }

    [Display(Name = "السيرة الذاتية")]
    public string? Bio { get; set; }

    [Display(Name = "التخصص")]
    public string? Specialization { get; set; }

    [Display(Name = "رقم الهاتف")]
    public string? Phone { get; set; }

    [Display(Name = "البريد الإلكتروني")]
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Course> Courses { get; set; } = new List<Course>();
}
