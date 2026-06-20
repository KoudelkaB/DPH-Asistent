using Dph.Core.Domain;
using Dph.Core.Persistence;

namespace Dph.Core.Tests;

public sealed class DphRepositoryTests
{
    [Fact]
    public async Task Saves_Ares_Cache_And_Counterparty_Name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        await repository.SaveAresCacheAsync(new AresSubject("27082440", "Alza.cz a.s.", "CZ27082440", new DateOnly(2026, 6, 20)));
        var cached = await repository.LoadAresCacheAsync("27082440");
        Assert.NotNull(cached);

        var counterparty = new Counterparty
        {
            Name = "Alza hardware",
            Ico = cached!.Ico,
            Dic = cached.Dic,
            Role = CounterpartyRole.Supplier
        };

        await repository.SaveCounterpartyAsync(counterparty);
        var loaded = await repository.LoadCounterpartiesAsync();

        Assert.Single(loaded);
        Assert.Equal("Alza hardware", loaded[0].Name);
    }

    [Fact]
    public async Task Saves_And_Updates_App_Setting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        await repository.SaveSettingAsync("export_directory", "/tmp/dph-a");
        await repository.SaveSettingAsync("export_directory", "/tmp/dph-b");

        Assert.Equal("/tmp/dph-b", await repository.LoadSettingAsync("export_directory"));
    }

    [Fact]
    public async Task Finds_Duplicate_Invoice_By_Evidence_Number_And_Counterparty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        await repository.SavePeriodAsync(period);
        await repository.SaveInvoiceAsync(new InvoiceLine
        {
            PeriodId = period.Id,
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = "INV-001",
            CounterpartyName = "Dodavatel",
            CounterpartyDic = "CZ12345678",
            TaxableSupplyDate = new DateOnly(2026, 5, 15),
            TaxBaseCzk = 100m,
            VatCzk = 21m
        });

        var duplicate = await repository.FindDuplicateInvoiceReferenceAsync(new InvoiceLine
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = " inv-001 ",
            CounterpartyName = "Dodavatel",
            CounterpartyDic = "cz12345678"
        });

        Assert.NotNull(duplicate);
        Assert.Equal("05/2026", duplicate!.PeriodLabel);
    }

    [Fact]
    public async Task Does_Not_Treat_Same_Evidence_Number_For_Different_Counterparty_As_Duplicate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        await repository.SavePeriodAsync(period);
        await repository.SaveInvoiceAsync(new InvoiceLine
        {
            PeriodId = period.Id,
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = "INV-001",
            CounterpartyName = "Dodavatel A",
            CounterpartyDic = "CZ12345678",
            TaxableSupplyDate = new DateOnly(2026, 5, 15),
            TaxBaseCzk = 100m,
            VatCzk = 21m
        });

        var duplicate = await repository.FindDuplicateInvoiceReferenceAsync(new InvoiceLine
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = "INV-001",
            CounterpartyName = "Dodavatel B",
            CounterpartyDic = "CZ87654321"
        });

        Assert.Null(duplicate);
    }

    [Fact]
    public async Task Does_Not_Treat_Issued_And_Received_With_Same_Number_As_Duplicate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        await repository.SavePeriodAsync(period);
        await repository.SaveInvoiceAsync(new InvoiceLine
        {
            PeriodId = period.Id,
            Kind = InvoiceKind.IssuedDomestic,
            EvidenceNumber = "2026-001",
            CounterpartyName = "Partner",
            CounterpartyDic = "CZ12345678",
            TaxableSupplyDate = new DateOnly(2026, 5, 15),
            TaxBaseCzk = 100m,
            VatCzk = 21m
        });

        var duplicate = await repository.FindDuplicateInvoiceReferenceAsync(new InvoiceLine
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = "2026-001",
            CounterpartyName = "Partner",
            CounterpartyDic = "CZ12345678"
        });

        Assert.Null(duplicate);
    }
}
