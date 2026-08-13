namespace ECert.Services;

public sealed record CategoryIconOption(string ClassName, string Label);

public static class CategoryIconCatalog
{
    public static IReadOnlyList<CategoryIconOption> Options { get; } = new List<CategoryIconOption>
    {
        new("bi bi-mortarboard-fill", "التعليم والتدريب"),
        new("bi bi-heart-pulse-fill", "الصحة والطب"),
        new("bi bi-code-slash", "البرمجة والتقنية"),
        new("bi bi-briefcase-fill", "الأعمال والإدارة"),
        new("bi bi-bar-chart-line-fill", "التحليل والبيانات"),
        new("bi bi-cash-coin", "المالية والمحاسبة"),
        new("bi bi-megaphone-fill", "التسويق والمبيعات"),
        new("bi bi-people-fill", "الموارد البشرية"),
        new("bi bi-translate", "اللغات"),
        new("bi bi-camera-reels-fill", "الإعلام والإنتاج"),
        new("bi bi-palette-fill", "التصميم والفنون"),
        new("bi bi-cpu-fill", "الذكاء الاصطناعي"),
        new("bi bi-shield-check", "الأمن والسلامة"),
        new("bi bi-truck", "اللوجستيات والنقل"),
        new("bi bi-building", "الهندسة والإنشاء"),
        new("bi bi-lightning-charge-fill", "الطاقة"),
        new("bi bi-tools", "المهارات المهنية"),
        new("bi bi-globe2", "السفر والضيافة")
    };

    public static bool IsAllowed(string? iconClass) =>
        !string.IsNullOrWhiteSpace(iconClass) &&
        Options.Any(option => string.Equals(option.ClassName, iconClass, StringComparison.Ordinal));
}
