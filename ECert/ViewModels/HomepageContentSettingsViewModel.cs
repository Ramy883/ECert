using System.ComponentModel.DataAnnotations;

namespace ECert.ViewModels;

public sealed class HomepageContentSettingsViewModel
{
    [Display(Name = "عنوان صفحة من نحن")]
    public string AboutTitle { get; set; } = "من نحن";

    [Display(Name = "نبذة مختصرة")]
    public string AboutSummary { get; set; } = string.Empty;

    [Display(Name = "المحتوى التفصيلي")]
    public string AboutContent { get; set; } = string.Empty;

    [Display(Name = "إظهار الفوتر بالكامل")]
    public bool FooterEnabled { get; set; } = true;

    [Display(Name = "إظهار عمود الروابط السريعة")]
    public bool FooterShowQuickLinks { get; set; } = true;

    [Display(Name = "إظهار عمود التواصل")]
    public bool FooterShowContact { get; set; } = true;

    [Display(Name = "إظهار عمود عن المركز")]
    public bool FooterShowAboutLinks { get; set; } = true;

    [Display(Name = "إظهار أيقونات وروابط التواصل الاجتماعي")]
    public bool FooterShowSocial { get; set; } = true;
}
