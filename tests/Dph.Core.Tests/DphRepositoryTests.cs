using System.Globalization;
using Dph.Core.Domain;
using Dph.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Dph.Core.Tests;

public sealed class DphRepositoryTests
{
    [Fact]
    public async Task Migrates_V010_Database_Linking_Vat_Rows_And_Locking_Inserted_Invoices()
    {
        // DB přesně ve schématu vydané verze 0.1.0: bez issued_invoice_id, bez zámků a souhrnů
        // vydaných faktur; vazba faktura → řádek přiznání jen shodou čísla dokladu.
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                create table periods (id integer primary key, year integer not null, month integer not null, submission_date text not null, form_type text not null, imported_at text null, exported_at text null, changed_at text null, unique(year, month));
                create table invoice_lines (id integer primary key, period_id integer not null, kind text not null, counterparty_id integer null, counterparty_name text not null, counterparty_dic text null, evidence_number text not null, taxable_supply_date text not null, tax_base_czk text not null, vat_czk text not null, currency text not null, foreign_amount text null, exchange_rate text null, vat_rate text not null, partial_deduction integer not null default 0, note text null);
                create table issued_invoices (id integer primary key, number text not null unique, issue_date text not null, taxable_supply_date text not null, due_date text not null, customer_id integer null, customer_name text not null, customer_ico text null, customer_dic text null, customer_street text null, customer_house_number text null, customer_city text null, customer_postal_code text null, customer_country text not null, currency text not null, variable_symbol text null, payment_method text null, intro_text text null, note text null, footer text null, created_at text not null);
                insert into periods (year, month, submission_date, form_type) values (2026, 5, '2026-06-20', 'B');
                insert into issued_invoices (number, issue_date, taxable_supply_date, due_date, customer_name, customer_country, currency, created_at) values ('20260001', '2026-05-31', '2026-05-31', '2026-06-14', 'Odběratel s.r.o.', 'Česká republika', 'CZK', '2026-05-31T10:00:00Z');
                insert into invoice_lines (period_id, kind, counterparty_name, evidence_number, taxable_supply_date, tax_base_czk, vat_czk, currency, vat_rate) values (1, 'IssuedDomestic', 'Odběratel s.r.o.', '20260001', '2026-05-31', '1000', '210', 'CZK', 'Standard21');
                insert into invoice_lines (period_id, kind, counterparty_name, counterparty_dic, evidence_number, taxable_supply_date, tax_base_czk, vat_czk, currency, vat_rate) values (1, 'ReceivedDomesticWithVat', 'Dodavatel', 'CZ27082440', 'FV-77', '2026-05-12', '500', '105', 'CZK', 'Standard21');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        // Řádek vydané faktury se naváže podle čísla dokladu, přijatý zůstane bez vazby.
        Assert.Equal([1L], await repository.LoadPeriodIdsForIssuedInvoiceAsync(1));
        var lines = await repository.LoadInvoicesAsync(1);
        Assert.Equal(1, lines.Single(x => x.Kind == InvoiceKind.IssuedDomestic).IssuedInvoiceId);
        Assert.Null(lines.Single(x => x.Kind == InvoiceKind.ReceivedDomesticWithVat).IssuedInvoiceId);

        // Faktura vložená do přiznání dostane zámek (0.1.0 znala jen zámek období).
        var invoice = (await repository.LoadIssuedInvoicesAsync()).Single();
        Assert.NotNull(invoice.VatInsertedAt);
        Assert.True(invoice.IsLockedByHistory);

        // Opakovaná inicializace je neškodná (migrace už neběží).
        await repository.InitializeAsync();
        Assert.Equal(invoice.VatInsertedAt, (await repository.LoadIssuedInvoicesAsync()).Single().VatInsertedAt);
    }

    [Fact]
    public async Task Persists_Decimals_Independently_Of_Current_Culture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // Save under a comma-decimal culture, read under invariant: must not corrupt the value.
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            var repository = new DphRepository(path);
            await repository.InitializeAsync();
            var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
            await repository.SavePeriodAsync(period);
            await repository.SaveInvoiceAsync(new InvoiceLine
            {
                PeriodId = period.Id,
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "INV-1",
                CounterpartyName = "Dodavatel",
                TaxableSupplyDate = new DateOnly(2026, 5, 15),
                TaxBaseCzk = 1234.56m,
                VatCzk = 259.26m,
                ForeignAmount = 50.25m,
                ExchangeRate = 24.567m
            });

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var loaded = (await repository.LoadInvoicesAsync(period.Id)).Single();
            Assert.Equal(1234.56m, loaded.TaxBaseCzk);
            Assert.Equal(259.26m, loaded.VatCzk);
            Assert.Equal(50.25m, loaded.ForeignAmount);
            Assert.Equal(24.567m, loaded.ExchangeRate);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

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
            Street = "Jankovcova",
            HouseNumber = "1522/53",
            City = "Praha",
            PostalCode = "17000",
            Role = CounterpartyRole.Supplier
        };

        await repository.SaveCounterpartyAsync(counterparty);
        var loaded = await repository.LoadCounterpartiesAsync();

        Assert.Single(loaded);
        Assert.Equal("Alza hardware", loaded[0].Name);
        Assert.Equal("Jankovcova", loaded[0].Street);
        Assert.Equal("1522/53", loaded[0].HouseNumber);
        Assert.Equal("Praha", loaded[0].City);
        Assert.Equal("17000", loaded[0].PostalCode);
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
    public async Task Marks_Period_As_Imported_And_Exported()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        await repository.SavePeriodAsync(period);
        await repository.MarkPeriodImportedAsync(period.Id, new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));
        await repository.MarkPeriodExportedAsync(period.Id, new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

        var loaded = await repository.LoadPeriodsAsync();

        Assert.True(loaded.Single().IsLockedByHistory);
        Assert.NotNull(loaded.Single().ImportedAt);
        Assert.NotNull(loaded.Single().ExportedAt);
    }

    [Fact]
    public async Task Change_After_Export_Is_Tracked_And_Reset_By_Next_Export()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        await repository.SavePeriodAsync(period);
        await repository.MarkPeriodExportedAsync(period.Id, new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        await repository.MarkPeriodChangedAsync(period.Id, new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero));

        var changed = (await repository.LoadPeriodsAsync()).Single();
        Assert.True(changed.IsLockedByHistory);
        Assert.NotNull(changed.ExportedAt);
        Assert.True(changed.HasPendingChanges);

        // Další export odráží aktuální stav, takže příznak změny zmizí (ale export zůstává).
        await repository.MarkPeriodExportedAsync(period.Id, new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

        var reexported = (await repository.LoadPeriodsAsync()).Single();
        Assert.NotNull(reexported.ExportedAt);
        Assert.False(reexported.HasPendingChanges);
    }

    [Fact]
    public async Task Deletes_Period_With_Its_Invoices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        await repository.SavePeriodAsync(period);
        await repository.SaveInvoiceAsync(new InvoiceLine
        {
            PeriodId = period.Id,
            IssuedInvoiceId = 42,
            Kind = InvoiceKind.IssuedDomestic,
            EvidenceNumber = "2026-001",
            CounterpartyName = "Odběratel",
            CounterpartyDic = "CZ12345678",
            TaxableSupplyDate = new DateOnly(2026, 5, 15),
            TaxBaseCzk = 100m,
            VatCzk = 21m
        });

        var loaded = (await repository.LoadInvoicesAsync(period.Id)).Single();
        Assert.Equal(42, loaded.IssuedInvoiceId);
        Assert.Equal([period.Id], await repository.LoadPeriodIdsForIssuedInvoiceAsync(42));

        await repository.DeletePeriodAsync(period.Id);

        Assert.Empty(await repository.LoadPeriodsAsync());
        Assert.Empty(await repository.LoadInvoicesAsync(period.Id));
    }

    [Fact]
    public async Task Saves_Partial_Deduction_On_Invoice()
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
            EvidenceNumber = "INV-POMER",
            CounterpartyName = "Dodavatel",
            CounterpartyDic = "CZ12345678",
            TaxableSupplyDate = new DateOnly(2026, 5, 15),
            TaxBaseCzk = 20_000m,
            VatCzk = 2_100m,
            PartialDeduction = true
        });

        var loaded = await repository.LoadInvoicesAsync(period.Id);

        Assert.True(loaded.Single().PartialDeduction);
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
