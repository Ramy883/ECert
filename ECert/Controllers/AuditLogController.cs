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
            totalInDb = await _db.AuditLogs.IgnoreQueryFilters().CountAsync();
            _logger.LogInformation("AuditLog page: user={User} role={Role} totalAll={Total}",
                User.Identity?.Name, roleName, totalInDb);

            // Try EF query first.
            IQueryable<AuditLog> query = _db.AuditLogs.IgnoreQueryFilters().AsQueryable();
            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrEmpty(action))
                query = query.Where(a => a.Action == action);

            logs = await query.OrderByDescending(a => a.Timestamp).Take(200).ToListAsync();
            _logger.LogInformation("EF query returned {Count} rows for entityType={ET} action={A}",
                logs.Count, entityType ?? "(all)", action ?? "(all)");

            // If EF unexpectedly returned empty while the table has rows, fall back to raw SQL
            // so the page is never silently blank.
            if (logs.Count == 0 && totalInDb > 0 && string.IsNullOrEmpty(entityType) && string.IsNullOrEmpty(action))
            {
                _logger.LogWarning("EF empty result with {Total} rows in DB; falling back to raw SQL", totalInDb);
                try
                {
                    var raw = await _db.AuditLogs
                        .FromSqlRaw(@"SELECT `AuditLogId`, `UserName`, `Action`, `EntityType`,
                                              `EntityId`, `OldValues`, `NewValues`, `Timestamp`, `IpAddress`
                                         FROM `AuditLogs`
                                         ORDER BY `Timestamp` DESC
                                         LIMIT 200")
                        .IgnoreQueryFilters()
                        .ToListAsync();
                    if (raw.Count > 0)
                    {
                        logs = raw;
                        _logger.LogInformation("Raw SQL fallback returned {Count} rows", logs.Count);
                    }
                }
                catch (Exception rawEx)
                {
                    _logger.LogError(rawEx, "Raw SQL fallback failed");
                    errorMessage = $"EF returned 0 rows; raw SQL fallback failed: {rawEx.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
                errorMessage += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            _logger.LogError(ex, "AuditLog page threw for user={User} role={Role}",
                User.Identity?.Name, roleName);
        }

        // Build a list of distinct values actually present in DB so the filter dropdowns match reality.
        var distinctActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinctEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var all = await _db.AuditLogs.IgnoreQueryFilters()
                .Select(a => new { a.Action, a.EntityType })
                .ToListAsync();
            foreach (var r in all)
            {
                if (!string.IsNullOrEmpty(r.Action)) distinctActions.Add(r.Action);
                if (!string.IsNullOrEmpty(r.EntityType)) distinctEntities.Add(r.EntityType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate distinct actions/entities for filter dropdowns");
        }

        ViewBag.Logs = logs;
        ViewBag.TotalCount = totalInDb;
        ViewBag.EntityType = entityType;
        ViewBag.Action = action;
        ViewBag.Error = errorMessage;
        ViewBag.ActorRole = roleName;
        ViewBag.AvailableActions = distinctActions.OrderBy(x => x).ToList();
        ViewBag.AvailableEntities = distinctEntities.OrderBy(x => x).ToList();
        return View();
    }
}
