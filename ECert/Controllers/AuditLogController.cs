using ECert.Data;
using ECert.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class AuditLogController : Controller
{
    private readonly ECertDbContext _db;
    private readonly ILogger<AuditLogController> _logger;
    public AuditLogController(ECertDbContext db, ILogger<AuditLogController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    public async Task<IActionResult> Index(string? entityType, string? action)
    {
        var roleName = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;
        if (!HasPermission("view-audit-log") && roleName != "SuperAdmin") return Forbid();

        int totalInDb = 0;
        List<AuditLog> logs = new();
        string? errorMessage = null;

        try
        {
            // Always measure the total to surface on the page (covers empty cases too).
            totalInDb = await _db.AuditLogs.AsNoTracking().CountAsync();
            _logger.LogInformation("AuditLog query: user={User} role={Role} totalAll={Total}", User.Identity?.Name, roleName, totalInDb);

            var query = _db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);

            logs = await query.OrderByDescending(a => a.Timestamp).Take(200).ToListAsync();
        }
        catch (Exception ex)
        {
            // Surface the *real* reason (db connection, schema, mapping) so it's never silently empty again.
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
                errorMessage += $"  | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            _logger.LogError(ex, "AuditLog query failed for user {User} role {Role}", User.Identity?.Name, roleName);
        }

        ViewBag.Logs = logs;
        ViewBag.TotalCount = totalInDb;
        ViewBag.EntityType = entityType;
        ViewBag.Action = action;
        ViewBag.Error = errorMessage;
        ViewBag.ActorRole = roleName;
        return View();
    }
}
