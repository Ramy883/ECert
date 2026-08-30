using ECert.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class AuditLogController : Controller
{
    private readonly ECertDbContext _db;
    public AuditLogController(ECertDbContext db) => _db = db;

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public async Task<IActionResult> Index(string? entityType, string? action)
    {
        var roleName = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;
        if (!HasPermission("view-audit-log") && roleName != "SuperAdmin") return Forbid();
        var query = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
        ViewBag.Logs = await query.OrderByDescending(a => a.Timestamp).Take(200).ToListAsync();
        ViewBag.EntityType = entityType;
        ViewBag.Action = action;
        return View();
    }
}
