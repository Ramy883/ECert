using System.ComponentModel.DataAnnotations;

namespace ECert.Models;

public class SiteSetting
{
    [Key]
    public int SiteSettingId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Category { get; set; } = "General";
}

public class HeroSlide
{
    [Key]
    public int HeroSlideId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Button1Text { get; set; }
    public string? Button1Url { get; set; }
    public string? Button2Text { get; set; }
    public string? Button2Url { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HeroAnimatedText
{
    [Key]
    public int HeroAnimatedTextId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SocialLink
{
    [Key]
    public int SocialLinkId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public string? Url { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ContactInfo
{
    [Key]
    public int ContactInfoId { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? WorkingHours { get; set; }
    public bool ShowPhone { get; set; } = true;
    public bool ShowMobile { get; set; } = true;
    public bool ShowEmail { get; set; } = true;
    public bool ShowWebsite { get; set; } = false;
    public bool ShowAddress { get; set; } = true;
    public bool ShowWorkingHours { get; set; } = true;
}

public class StatCard
{
    [Key]
    public int StatCardId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string IconClass { get; set; } = "bi-star";
    public string Color { get; set; } = "#2563eb";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDynamic { get; set; } = true;
    public string? DynamicSource { get; set; } // Courses, Instructors, Trainees, Certificates
    public int StaticValue { get; set; }
}

public class HomepageSection
{
    [Key]
    public int HomepageSectionId { get; set; }
    public string SectionKey { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
}

public class ThemeSetting
{
    [Key]
    public int ThemeSettingId { get; set; }
    public string PrimaryColor { get; set; } = "#2563eb";
    public string SecondaryColor { get; set; } = "#f59e0b";
    public string ButtonColor { get; set; } = "#2563eb";
    public string NavbarColor { get; set; } = "#0f172a";
    public string FooterColor { get; set; } = "#111827";
}
