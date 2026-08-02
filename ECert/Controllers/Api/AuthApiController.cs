using System.Security.Claims;
using ECert.Data;
using ECert.Models.ApiModels;
using ECert.Models.ViewModels;
using ECert.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public AuthApiController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    [HttpPost("login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse.Fail("البيانات المدخلة غير صحيحة"));

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Username == model.Username && u.IsActive);

        if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            return Ok(ApiResponse.Fail("اسم المستخدم أو كلمة المرور غير صحيحة"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email)
        };

        var permissions = new List<string>();
        foreach (var ur in user.UserRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, ur.Role!.RoleName));
            foreach (var rp in ur.Role.RolePermissions)
            {
                claims.Add(new Claim("Permission", rp.Permission!.PermissionName));
                permissions.Add(rp.Permission.PermissionName);
            }
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(user.FullName, "Login", "User", user.UserId, ip: HttpContext.Connection.RemoteIpAddress?.ToString());

        var roleName = user.UserRoles.FirstOrDefault()?.Role?.RoleName ?? "Admin";
        return Ok(ApiResponse<object>.Ok(new
        {
            userId = user.UserId,
            fullName = user.FullName,
            username = user.Username,
            email = user.Email,
            role = roleName,
            permissions
        }, "تم تسجيل الدخول بنجاح"));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var user = new
        {
            name = User.Identity?.Name,
            id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            email = User.FindFirst(ClaimTypes.Email)?.Value,
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
            permissions = User.FindAll("Permission").Select(c => c.Value).ToList()
        };
        return Ok(ApiResponse<object>.Ok(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(ApiResponse.Ok("تم تسجيل الخروج بنجاح"));
    }
}
