namespace Dph.Core.Domain;

// Lhůta pro podání přiznání i kontrolního hlášení měsíčního plátce: 25. den po skončení
// zdaňovacího období (§136 odst. 4 daňového řádu, §101e ZDPH). Připadne-li na sobotu, neděli
// nebo svátek, je posledním dnem lhůty nejbližší následující pracovní den (§33 odst. 4 DŘ).
// Po lhůtě už nelze podat opravné přiznání/KH – opravy jdou jen dodatečným přiznáním (§141 DŘ)
// a následným kontrolním hlášením (§101f odst. 2 ZDPH).
public static class FilingDeadline
{
    public static DateOnly For(int year, int month)
    {
        var deadline = new DateOnly(year, month, 25).AddMonths(1);
        while (IsWeekend(deadline) || IsCzechHoliday(deadline))
        {
            deadline = deadline.AddDays(1);
        }

        return deadline;
    }

    public static bool IsAfterDeadline(VatPeriod period, DateOnly date) => date > For(period.Year, period.Month);

    private static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static bool IsCzechHoliday(DateOnly date)
    {
        if ((date.Month, date.Day) is (1, 1) or (5, 1) or (5, 8) or (7, 5) or (7, 6)
            or (9, 28) or (10, 28) or (11, 17) or (12, 24) or (12, 25) or (12, 26))
        {
            return true;
        }

        var easterSunday = EasterSunday(date.Year);
        return date == easterSunday.AddDays(-2) || date == easterSunday.AddDays(1); // Velký pátek, Velikonoční pondělí
    }

    // Anonymní gregoriánský algoritmus (Meeus/Jones/Butcher).
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
