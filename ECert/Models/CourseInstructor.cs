using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class CourseInstructor
{
    [Key]
    public int CourseInstructorId { get; set; }

    public int CourseId { get; set; }
    public Course? Course { get; set; }

    public int InstructorId { get; set; }
    public Instructor? Instructor { get; set; }

    public int SortOrder { get; set; }
}
