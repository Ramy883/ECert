using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class Role
{
    [Key]
    public int RoleId { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "اسم الدور")]
    public string RoleName { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    public bool IsSystem { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission
{
    [Key]
    public int PermissionId { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "اسم الصلاحية")]
    public string PermissionName { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission
{
    [Key]
    public int RolePermissionId { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

public class UserRole
{
    [Key]
    public int UserRoleId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }
}
