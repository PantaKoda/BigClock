using System.Globalization;
using Xunit;

namespace BigClock.Tests;

public class CalendarMonthTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Theory]
    [InlineData(DayOfWeek.Monday, 2026, 10, 26)] // 1 Nov 2026 is a Sunday.
    [InlineData(DayOfWeek.Sunday, 2026, 11, 1)]
    [InlineData(DayOfWeek.Saturday, 2026, 10, 31)]
    public void Grid_starts_on_the_first_day_of_the_week(DayOfWeek firstDay, int year, int month, int day)
    {
        var cells = CalendarMonth.Build(2026, 11, firstDay, Today);

        Assert.Equal(new DateOnly(year, month, day), cells[0].Date);
        Assert.Equal(firstDay, cells[0].Date.DayOfWeek);
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 12, 31)]
    public void Every_day_of_the_month_appears_once_in_six_weeks(int year, int month, int days)
    {
        var cells = CalendarMonth.Build(year, month, DayOfWeek.Monday, Today);

        Assert.Equal(CalendarMonth.CellCount, cells.Count);
        Assert.Equal(Enumerable.Range(1, days), cells.Where(c => c.IsInMonth).Select(c => c.Day));
    }

    [Fact]
    public void Only_today_is_marked()
    {
        var cells = CalendarMonth.Build(2026, 11, DayOfWeek.Monday, Today);

        Assert.Equal(Today, Assert.Single(cells, c => c.IsToday).Date);
    }

    [Fact]
    public void Headers_follow_the_culture_week_start()
    {
        Assert.Equal("Mon", CalendarMonth.DayHeaders(new CultureInfo("en-GB").DateTimeFormat)[0]);
        Assert.Equal("Sun", CalendarMonth.DayHeaders(new CultureInfo("en-US").DateTimeFormat)[0]);
    }
}
