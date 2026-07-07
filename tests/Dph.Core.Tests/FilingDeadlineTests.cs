using Dph.Core.Domain;

namespace Dph.Core.Tests;

public sealed class FilingDeadlineTests
{
    [Fact]
    public void Deadline_Is_25th_Of_Following_Month()
    {
        // 25.06.2026 je čtvrtek – žádný posun.
        Assert.Equal(new DateOnly(2026, 6, 25), FilingDeadline.For(2026, 5));
    }

    [Fact]
    public void Weekend_Deadline_Moves_To_Next_Working_Day()
    {
        // 25.07.2026 je sobota → pondělí 27.07.2026.
        Assert.Equal(new DateOnly(2026, 7, 27), FilingDeadline.For(2026, 6));
    }

    [Fact]
    public void Easter_Monday_Deadline_Moves_To_Tuesday()
    {
        // 25.04.2011 bylo Velikonoční pondělí → úterý 26.04.2011.
        Assert.Equal(new DateOnly(2011, 4, 26), FilingDeadline.For(2011, 3));
    }

    [Fact]
    public void Fixed_Holiday_After_Weekend_Extends_Shift()
    {
        // 25.12.2027 je sobota, 26.12. neděle (a svátek), 27.12. pondělí – pracovní den.
        Assert.Equal(new DateOnly(2027, 12, 27), FilingDeadline.For(2027, 11));
    }

    [Fact]
    public void After_Deadline_Comparison_Uses_Shifted_Date()
    {
        var period = new VatPeriod { Year = 2026, Month = 5 };
        Assert.False(FilingDeadline.IsAfterDeadline(period, new DateOnly(2026, 6, 25)));
        Assert.True(FilingDeadline.IsAfterDeadline(period, new DateOnly(2026, 7, 3)));
    }
}
