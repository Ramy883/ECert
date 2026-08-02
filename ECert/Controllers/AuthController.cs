using System.Security.Claims;
using ECert.Data;
using ECert.Models.ViewModels;
using ECert.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

public class AuthController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    public AuthController(ECertDbContext db, AuditLogService audit) { _db = db; _audit = audit; }

    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Username == model.Username && u.IsActive);

        if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "اسم المستخدم أو كلمة المرور غير صحيحة");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email)
        };

        foreach (var ur in user.UserRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, ur.Role!.RoleName));
            foreach (var rp in ur.Role.RolePermissions)
                claims.Add(new Claim("Permission", rp.Permission!.PermissionName));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(user.FullName, "Login", "User", user.UserId, ip: HttpContext.Connection.RemoteIpAddress?.ToString());

        // Redirect based on role
        var roleName = user.UserRoles.FirstOrDefault()?.Role?.RoleName ?? "Admin";
        return roleName switch
        {
            "SuperAdmin" => RedirectToAction("Index", "Dashboard"),
            "Admin" => RedirectToAction("Index", "ManagerDashboard"),
            "Media" => RedirectToAction("Index", "MediaDashboard"),
            "Finance" => RedirectToAction("Index", "FinanceDashboard"),
            _ => RedirectToAction("Index", "Dashboard")
        };
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
