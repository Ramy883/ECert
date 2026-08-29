using ECert.Data;
using ECert.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

/// <summary>
/// Populates layout data for every MVC view so the public navbar/footer always receives
/// theme settings, contact info, and social links regardless of which controller rendered the page.
/// This fixes missing footer/social content on public pages outside Home/Index.
/// </summary>
public sealed class SiteLayoutDataFilter : IAsyncResultFilter
{
    private readonly ECertDbContext _db;

    public SiteLayoutDataFilter(ECertDbContext db) => _db = db;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Controller is not Controller controller || context.Result is not ViewResult)
        {
            await next();
            return;
        }

        if (!controller.ViewData.ContainsKey("Theme"))
            controller.ViewData["Theme"] = await _db.ThemeSettings.FirstOrDefaultAsync() ?? new ThemeSetting();

        if (!controller.ViewData.ContainsKey("SiteSettings"))
            controller.ViewData["SiteSettings"] = await _db.SiteSettings.ToListAsync();

        if (!controller.ViewData.ContainsKey("SocialLinks"))
            controller.ViewData["SocialLinks"] = await _db.SocialLinks
                .Where(link => link.IsActive)
                .OrderBy(link => link.SortOrder)
                .ToListAsync();

        if (!controller.ViewData.ContainsKey("ContactInfo"))
            controller.ViewData["ContactInfo"] = await _db.ContactInfos.FirstOrDefaultAsync();

        await next();
    }
}
