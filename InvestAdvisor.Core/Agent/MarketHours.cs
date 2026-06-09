namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Simple NYSE/NASDAQ regular-session check: Monday–Friday, 09:30–16:00 in the user's
/// configured timezone (default "America/New_York"). Does not account for holidays in v1 —
/// the cost of generating an extra Scheduled-trigger advice on a market holiday is a single
/// LLM call, which is acceptable for a personal app.
/// </summary>
public static class MarketHours
{
    private static readonly TimeOnly OpenLocal = new(9, 30);
    private static readonly TimeOnly CloseLocal = new(16, 0);

    public static bool IsOpenNY(DateTime utcNow, string timeZoneId)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fall back to UTC if the user has misconfigured the timezone id.
            tz = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);

        if (local.DayOfWeek == DayOfWeek.Saturday || local.DayOfWeek == DayOfWeek.Sunday)
            return false;

        var t = TimeOnly.FromDateTime(local);
        return t >= OpenLocal && t < CloseLocal;
    }
}
