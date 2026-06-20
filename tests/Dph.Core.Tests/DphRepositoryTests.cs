using Dph.Core.Domain;
using Dph.Core.Persistence;

namespace Dph.Core.Tests;

public sealed class DphRepositoryTests
{
    [Fact]
    public async Task Saves_Ares_Cache_And_Allows_Custom_Counterparty_Name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        await repository.SaveAresCacheAsync(new AresSubject("27082440", "Alza.cz a.s.", "CZ27082440", new DateOnly(2026, 6, 20)));
        var cached = await repository.LoadAresCacheAsync("27082440");

        var counterparty = new Counterparty
        {
            CustomName = "Alza hardware",
            OfficialName = cached!.OfficialName,
            Ico = cached.Ico,
            Dic = cached.Dic,
            Role = CounterpartyRole.Supplier
        };

        await repository.SaveCounterpartyAsync(counterparty);
        var loaded = await repository.LoadCounterpartiesAsync();

        Assert.Single(loaded);
        Assert.Equal("Alza hardware", loaded[0].CustomName);
        Assert.Equal("Alza.cz a.s.", loaded[0].OfficialName);
    }
}
