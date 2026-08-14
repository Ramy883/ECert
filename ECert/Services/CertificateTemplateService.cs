using System.Text.Json;
using ECert.Data;
using ECert.Models;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class CertificateTemplateService
{
    private const string DefaultElementOrder = "logo,title,body,trainee,course,date,additional,verification,signatures";
    private readonly ECertDbContext _db;

    public CertificateTemplateService(ECertDbContext db) => _db = db;

    public static CertificateTemplateSettings DefaultTemplate() => new()
    {
        CertificateTitle = "شهادة إتمام",
        MainText = "تشهد الجهة بأن المتدرب قد أتم متطلبات الدورة التدريبية بنجاح.",
        CenterName = "مركز التدريب المهني",
        AdditionalText = "مع تمنياتنا بدوام التوفيق والنجاح.",
        VerificationLabel = "يمكن التحقق من صحة الشهادة عبر الرابط التالي",
        PrimaryColor = "#173B67",
        AccentColor = "#C9A227",
        TextColor = "#1F2937",
        ShowVerificationCode = true,
        ShowCertificateNumber = true,
        ShowIssueDate = true,
        ShowVerificationUrl = true,
        ElementOrder = DefaultElementOrder,
        TemplateVersion = 1,
        UpdatedAt = DateTime.Now
    };

    public async Task<CertificateTemplateSettings> GetCurrentAsync()
    {
        var stored = await _db.CertificateTemplates.OrderByDescending(t => t.TemplateVersion).FirstOrDefaultAsync();
        return Normalize(stored?.Clone() ?? DefaultTemplate());
    }

    public CertificateTemplateSettings FromSnapshot(string? json, out bool isLegacy)
    {
        isLegacy = string.IsNullOrWhiteSpace(json);
        if (!isLegacy)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<CertificateTemplateSettings>(json!);
                if (snapshot != null)
                    return Normalize(snapshot);
            }
            catch (JsonException)
            {
                // Invalid historical snapshots use the immutable default below.
            }
        }

        return DefaultTemplate();
    }

    public string Serialize(CertificateTemplateSettings settings)
        => JsonSerializer.Serialize(Normalize(settings));

    public CertificateTemplateSettings Normalize(CertificateTemplateSettings settings)
    {
        var normalized = settings.Clone();
        normalized.CertificateTitle = Limit(normalized.CertificateTitle, 160, "شهادة إتمام");
        normalized.MainText = Limit(normalized.MainText, 2000, "تشهد الجهة بأن المتدرب قد أتم متطلبات الدورة التدريبية بنجاح.");
        normalized.CenterName = Limit(normalized.CenterName, 200, "مركز التدريب المهني");
        normalized.AdditionalText = LimitNullable(normalized.AdditionalText, 1000);
        normalized.VerificationLabel = Limit(normalized.VerificationLabel, 200, "يمكن التحقق من صحة الشهادة عبر الرابط التالي");
        normalized.PrimaryColor = NormalizeColor(normalized.PrimaryColor, "#173B67");
        normalized.AccentColor = NormalizeColor(normalized.AccentColor, "#C9A227");
        normalized.TextColor = NormalizeColor(normalized.TextColor, "#1F2937");
        normalized.ElementOrder = NormalizeElementOrder(normalized.ElementOrder);
        normalized.TemplateVersion = Math.Max(1, normalized.TemplateVersion);
        return normalized;
    }

    private static string NormalizeElementOrder(string? order)
    {
        var allowed = new[] { "logo", "title", "body", "trainee", "course", "date", "additional", "verification", "signatures" };
        var requested = (order ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in allowed)
            if (!requested.Contains(item, StringComparer.OrdinalIgnoreCase)) requested.Add(item);

        return string.Join(',', requested);
    }

    private static string NormalizeColor(string? value, string fallback)
        => !string.IsNullOrWhiteSpace(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$")
            ? value
            : fallback;

    private static string Limit(string? value, int maxLength, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }

    private static string? LimitNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }
}
