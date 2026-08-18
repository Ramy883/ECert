namespace ECert.Models;

/// <summary>
/// Formats a course's inclusive date range for public-facing Arabic UI.
/// The stored dates remain available to administration and registration workflows.
/// </summary>
public static class CourseDurationFormatter
{
    public static string Format(DateTime? startDate, DateTime? endDate)
    {
        if (!startDate.HasValue || !endDate.HasValue)
            return "يحدد لاحقاً";

        var days = Math.Max(1, (endDate.Value.Date - startDate.Value.Date).Days + 1);

        if (days % 30 == 0)
            return FormatArabicUnit(days / 30, "شهر", "شهران", "أشهر", "شهراً");

        if (days % 7 == 0)
            return FormatArabicUnit(days / 7, "أسبوع", "أسبوعان", "أسابيع", "أسبوعاً");

        return FormatArabicUnit(days, "يوم", "يومان", "أيام", "يوماً");
    }

    private static string FormatArabicUnit(int value, string singular, string dual, string plural, string accusative)
    {
        return value switch
        {
            1 when singular == "يوم" => "يوم واحد",
            1 => $"{singular} واحد",
            2 => dual,
            >= 3 and <= 10 => $"{value} {plural}",
            _ => $"{value} {accusative}"
        };
    }
}
