using System.Globalization;

namespace BigClock;

public sealed record CalendarDay(DateOnly Date, bool IsInMonth, bool IsToday)
{
    public int Day => Date.Day;
}

public static class CalendarMonth
{
    public const int CellCount = 42;

    /// <summary>Six full weeks containing the month, so the grid never changes height.</summary>
    public static IReadOnlyList<CalendarDay> Build(int year, int month, DayOfWeek firstDayOfWeek, DateOnly today)
    {
        var first = new DateOnly(year, month, 1);
        var leadingDays = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var start = first.AddDays(-leadingDays);

        return Enumerable.Range(0, CellCount)
            .Select(offset => start.AddDays(offset))
            .Select(date => new CalendarDay(date, date.Year == year && date.Month == month, date == today))
            .ToList();
    }

    public static IReadOnlyList<string> DayHeaders(DateTimeFormatInfo format)
    {
        var names = format.AbbreviatedDayNames;
        return Enumerable.Range(0, 7)
            .Select(offset => names[((int)format.FirstDayOfWeek + offset) % 7])
            .ToList();
    }
}
