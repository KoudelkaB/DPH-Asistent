using Dph.Core.Domain;

namespace Dph.Core.Tests;

public sealed class TemplateDateTests
{
    [Theory]
    // Pravidelná faktura má DUZP na tentýž den v měsíci – kopie ho drží.
    [InlineData("2026-05-15", 2026, 6, "2026-06-15")]
    [InlineData("2026-05-01", 2026, 6, "2026-06-01")]
    // Faktura na 28., 29. nebo 30. zůstává na tomtéž dni, i když to byl poslední den zdrojového měsíce.
    [InlineData("2026-02-28", 2026, 3, "2026-03-28")]
    [InlineData("2024-02-29", 2024, 3, "2024-03-29")]
    [InlineData("2026-04-30", 2026, 5, "2026-05-30")]
    // Den, který v cílovém měsíci není, se zkrátí na poslední.
    [InlineData("2026-01-31", 2026, 2, "2026-02-28")]
    [InlineData("2026-01-30", 2026, 2, "2026-02-28")]
    [InlineData("2026-03-31", 2026, 4, "2026-04-30")]
    [InlineData("2026-03-31", 2026, 5, "2026-05-31")]
    // Posun přes přelom roku i zpět.
    [InlineData("2025-12-10", 2026, 1, "2026-01-10")]
    [InlineData("2026-06-20", 2026, 5, "2026-05-20")]
    public void Shifts_the_day_into_the_target_month(string source, int year, int month, string expected)
        => Assert.Equal(DateOnly.Parse(expected), TemplateDate.ShiftToMonth(DateOnly.Parse(source), year, month));

    [Fact]
    public void Keeps_the_date_when_the_target_month_is_its_own()
        => Assert.Equal(new DateOnly(2026, 5, 15), TemplateDate.ShiftToMonth(new DateOnly(2026, 5, 15), 2026, 5));
}
