using System.Globalization;
using Dph.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Dph.Core.Persistence;

public sealed class DphRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, """
            create table if not exists tax_subjects (
                id integer primary key,
                display_name text not null,
                dic text not null,
                ico text null,
                first_name text not null,
                last_name text not null,
                title text null,
                street text not null,
                house_number text null,
                city text not null,
                postal_code text not null,
                country text not null,
                email text null,
                phone text null,
                tax_office_code text not null,
                workplace_code text not null,
                data_box_id text null,
                activity_code text not null
            );
            create table if not exists counterparties (
                id integer primary key,
                name text not null,
                ico text null,
                dic text null,
                country_code text not null,
                street text null,
                house_number text null,
                city text null,
                postal_code text null,
                role text not null,
                ares_updated_at text null
            );
            create table if not exists periods (
                id integer primary key,
                year integer not null,
                month integer not null,
                submission_date text not null,
                form_type text not null,
                imported_at text null,
                exported_at text null,
                changed_at text null,
                unique(year, month)
            );
            create table if not exists invoice_lines (
                id integer primary key,
                period_id integer not null,
                kind text not null,
                counterparty_id integer null,
                counterparty_name text not null,
                counterparty_dic text null,
                evidence_number text not null,
                taxable_supply_date text not null,
                tax_base_czk text not null,
                vat_czk text not null,
                currency text not null,
                foreign_amount text null,
                exchange_rate text null,
                vat_rate text not null,
                partial_deduction integer not null default 0,
                note text null
            );
            create table if not exists ares_cache (
                ico text primary key,
                official_name text not null,
                dic text null,
                updated_on text null,
                fetched_at text not null
            );
            create table if not exists exchange_rate_cache (
                currency_code text not null,
                rate_date text not null,
                amount integer not null,
                rate_czk text not null,
                fetched_at text not null,
                primary key(currency_code, rate_date)
            );
            create table if not exists app_settings (
                key text primary key,
                value text not null
            );
            create table if not exists issued_invoices (
                id integer primary key,
                number text not null unique,
                issue_date text not null,
                taxable_supply_date text not null,
                due_date text not null,
                customer_id integer null,
                customer_name text not null,
                customer_ico text null,
                customer_dic text null,
                customer_street text null,
                customer_house_number text null,
                customer_city text null,
                customer_postal_code text null,
                customer_country text not null,
                currency text not null,
                variable_symbol text null,
                payment_method text null,
                intro_text text null,
                note text null,
                footer text null,
                created_at text not null
            );
            create table if not exists issued_invoice_items (
                id integer primary key,
                invoice_id integer not null,
                description text not null,
                quantity text not null,
                unit text not null,
                unit_price_czk text not null,
                vat_rate text not null,
                sort_order integer not null default 0
            );
            """, cancellationToken);
        await EnsureColumnAsync(connection, "periods", "imported_at", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "periods", "exported_at", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "periods", "changed_at", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "invoice_lines", "partial_deduction", "integer not null default 0", cancellationToken);
        await EnsureColumnAsync(connection, "tax_subjects", "bank_account", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "tax_subjects", "iban", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "issued_invoices", "intro_text", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "counterparties", "street", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "counterparties", "house_number", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "counterparties", "city", "text null", cancellationToken);
        await EnsureColumnAsync(connection, "counterparties", "postal_code", "text null", cancellationToken);
    }

    public async Task<TaxSubject?> LoadTaxSubjectAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from tax_subjects order by id limit 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new TaxSubject
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                DisplayName = Text(reader, "display_name"),
                Dic = Text(reader, "dic"),
                Ico = NullableText(reader, "ico"),
                FirstName = Text(reader, "first_name"),
                LastName = Text(reader, "last_name"),
                Title = NullableText(reader, "title"),
                Street = Text(reader, "street"),
                HouseNumber = NullableText(reader, "house_number"),
                City = Text(reader, "city"),
                PostalCode = Text(reader, "postal_code"),
                Country = Text(reader, "country"),
                Email = NullableText(reader, "email"),
                Phone = NullableText(reader, "phone"),
                TaxOfficeCode = Text(reader, "tax_office_code"),
                WorkplaceCode = Text(reader, "workplace_code"),
                DataBoxId = NullableText(reader, "data_box_id"),
                ActivityCode = Text(reader, "activity_code"),
                BankAccount = NullableText(reader, "bank_account"),
                Iban = NullableText(reader, "iban")
            }
            : null;
    }

    public async Task SaveTaxSubjectAsync(TaxSubject subject, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into tax_subjects (id, display_name, dic, ico, first_name, last_name, title, street, house_number, city, postal_code, country, email, phone, tax_office_code, workplace_code, data_box_id, activity_code, bank_account, iban)
            values (1, $display_name, $dic, $ico, $first_name, $last_name, $title, $street, $house_number, $city, $postal_code, $country, $email, $phone, $tax_office_code, $workplace_code, $data_box_id, $activity_code, $bank_account, $iban)
            on conflict(id) do update set
                display_name=excluded.display_name, dic=excluded.dic, ico=excluded.ico, first_name=excluded.first_name, last_name=excluded.last_name,
                title=excluded.title, street=excluded.street, house_number=excluded.house_number, city=excluded.city, postal_code=excluded.postal_code,
                country=excluded.country, email=excluded.email, phone=excluded.phone, tax_office_code=excluded.tax_office_code,
                workplace_code=excluded.workplace_code, data_box_id=excluded.data_box_id, activity_code=excluded.activity_code,
                bank_account=excluded.bank_account, iban=excluded.iban
            """;
        Add(command, "$display_name", subject.DisplayName);
        Add(command, "$dic", subject.Dic);
        Add(command, "$ico", subject.Ico);
        Add(command, "$first_name", subject.FirstName);
        Add(command, "$last_name", subject.LastName);
        Add(command, "$title", subject.Title);
        Add(command, "$street", subject.Street);
        Add(command, "$house_number", subject.HouseNumber);
        Add(command, "$city", subject.City);
        Add(command, "$postal_code", subject.PostalCode);
        Add(command, "$country", subject.Country);
        Add(command, "$email", subject.Email);
        Add(command, "$phone", subject.Phone);
        Add(command, "$tax_office_code", subject.TaxOfficeCode);
        Add(command, "$workplace_code", subject.WorkplaceCode);
        Add(command, "$data_box_id", subject.DataBoxId);
        Add(command, "$activity_code", subject.ActivityCode);
        Add(command, "$bank_account", subject.BankAccount);
        Add(command, "$iban", subject.Iban);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<Counterparty>> LoadCounterpartiesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from counterparties order by name, dic, ico";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<Counterparty>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new Counterparty
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Name = Text(reader, "name"),
                Ico = NullableText(reader, "ico"),
                Dic = NullableText(reader, "dic"),
                CountryCode = Text(reader, "country_code"),
                Street = NullableText(reader, "street"),
                HouseNumber = NullableText(reader, "house_number"),
                City = NullableText(reader, "city"),
                PostalCode = NullableText(reader, "postal_code"),
                Role = Enum.Parse<CounterpartyRole>(Text(reader, "role")),
                AresUpdatedAt = ParseDateTimeOffset(NullableText(reader, "ares_updated_at"))
            });
        }

        return items;
    }

    public async Task<long> SaveCounterpartyAsync(Counterparty counterparty, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = counterparty.Id == 0
            ? """
              insert into counterparties (name, ico, dic, country_code, street, house_number, city, postal_code, role, ares_updated_at)
              values ($name, $ico, $dic, $country_code, $street, $house_number, $city, $postal_code, $role, $ares_updated_at)
              returning id
              """
            : """
              update counterparties set name=$name, ico=$ico, dic=$dic, country_code=$country_code,
                  street=$street, house_number=$house_number, city=$city, postal_code=$postal_code, role=$role, ares_updated_at=$ares_updated_at
              where id=$id
              returning id
              """;
        Add(command, "$id", counterparty.Id);
        Add(command, "$name", counterparty.Name);
        Add(command, "$ico", counterparty.Ico);
        Add(command, "$dic", counterparty.Dic);
        Add(command, "$country_code", counterparty.CountryCode);
        Add(command, "$street", counterparty.Street);
        Add(command, "$house_number", counterparty.HouseNumber);
        Add(command, "$city", counterparty.City);
        Add(command, "$postal_code", counterparty.PostalCode);
        Add(command, "$role", counterparty.Role.ToString());
        Add(command, "$ares_updated_at", counterparty.AresUpdatedAt?.ToString("O"));
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? counterparty.Id);
        counterparty.Id = id;
        return id;
    }

    public async Task<List<VatPeriod>> LoadPeriodsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from periods order by year desc, month desc";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<VatPeriod>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new VatPeriod
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Year = reader.GetInt32(reader.GetOrdinal("year")),
                Month = reader.GetInt32(reader.GetOrdinal("month")),
                SubmissionDate = DateOnly.Parse(Text(reader, "submission_date"), CultureInfo.InvariantCulture),
                FormType = Text(reader, "form_type"),
                ImportedAt = ParseDateTimeOffset(NullableText(reader, "imported_at")),
                ExportedAt = ParseDateTimeOffset(NullableText(reader, "exported_at")),
                ChangedAt = ParseDateTimeOffset(NullableText(reader, "changed_at"))
            });
        }

        return items;
    }

    public async Task<long> SavePeriodAsync(VatPeriod period, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into periods (id, year, month, submission_date, form_type, imported_at, exported_at, changed_at)
            values ($id, $year, $month, $submission_date, $form_type, $imported_at, $exported_at, $changed_at)
            on conflict(year, month) do update set
                submission_date=excluded.submission_date,
                form_type=excluded.form_type,
                imported_at=coalesce(excluded.imported_at, periods.imported_at),
                exported_at=coalesce(excluded.exported_at, periods.exported_at),
                changed_at=coalesce(excluded.changed_at, periods.changed_at)
            returning id
            """;
        Add(command, "$id", period.Id == 0 ? null : period.Id);
        Add(command, "$year", period.Year);
        Add(command, "$month", period.Month);
        Add(command, "$submission_date", period.SubmissionDate.ToString("yyyy-MM-dd"));
        Add(command, "$form_type", period.FormType);
        Add(command, "$imported_at", period.ImportedAt?.ToString("O"));
        Add(command, "$exported_at", period.ExportedAt?.ToString("O"));
        Add(command, "$changed_at", period.ChangedAt?.ToString("O"));
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? period.Id);
        period.Id = id;
        return id;
    }

    public async Task MarkPeriodImportedAsync(long periodId, DateTimeOffset importedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "update periods set imported_at=$imported_at where id=$id";
        Add(command, "$id", periodId);
        Add(command, "$imported_at", importedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkPeriodExportedAsync(long periodId, DateTimeOffset exportedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Export odráží aktuální stav, takže příznak změny mizí.
        command.CommandText = "update periods set exported_at=$exported_at, changed_at=null where id=$id";
        Add(command, "$id", periodId);
        Add(command, "$exported_at", exportedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Označí už podané období jako po podání upravené (zachová import/export příznak).
    public async Task MarkPeriodChangedAsync(long periodId, DateTimeOffset changedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "update periods set changed_at=$changed_at where id=$id";
        Add(command, "$id", periodId);
        Add(command, "$changed_at", changedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeletePeriodAsync(long periodId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteInvoices = connection.CreateCommand())
        {
            deleteInvoices.Transaction = (SqliteTransaction)transaction;
            deleteInvoices.CommandText = "delete from invoice_lines where period_id=$period_id";
            Add(deleteInvoices, "$period_id", periodId);
            await deleteInvoices.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deletePeriod = connection.CreateCommand())
        {
            deletePeriod.Transaction = (SqliteTransaction)transaction;
            deletePeriod.CommandText = "delete from periods where id=$id";
            Add(deletePeriod, "$id", periodId);
            await deletePeriod.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<InvoiceLine>> LoadInvoicesAsync(long periodId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from invoice_lines where period_id=$period_id order by taxable_supply_date, id";
        Add(command, "$period_id", periodId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<InvoiceLine>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new InvoiceLine
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                PeriodId = reader.GetInt64(reader.GetOrdinal("period_id")),
                Kind = Enum.Parse<InvoiceKind>(Text(reader, "kind")),
                CounterpartyId = NullableLong(reader, "counterparty_id"),
                CounterpartyName = Text(reader, "counterparty_name"),
                CounterpartyDic = NullableText(reader, "counterparty_dic"),
                EvidenceNumber = Text(reader, "evidence_number"),
                TaxableSupplyDate = DateOnly.Parse(Text(reader, "taxable_supply_date"), CultureInfo.InvariantCulture),
                TaxBaseCzk = ParseDecimal(Text(reader, "tax_base_czk")),
                VatCzk = ParseDecimal(Text(reader, "vat_czk")),
                Currency = Text(reader, "currency"),
                ForeignAmount = NullableDecimal(reader, "foreign_amount"),
                ExchangeRate = NullableDecimal(reader, "exchange_rate"),
                VatRate = Enum.Parse<VatRateKind>(Text(reader, "vat_rate")),
                PartialDeduction = Bool(reader, "partial_deduction"),
                Note = NullableText(reader, "note")
            });
        }

        return items;
    }

    public async Task<long> SaveInvoiceAsync(InvoiceLine invoice, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = invoice.Id == 0
            ? """
              insert into invoice_lines (period_id, kind, counterparty_id, counterparty_name, counterparty_dic, evidence_number, taxable_supply_date, tax_base_czk, vat_czk, currency, foreign_amount, exchange_rate, vat_rate, partial_deduction, note)
              values ($period_id, $kind, $counterparty_id, $counterparty_name, $counterparty_dic, $evidence_number, $taxable_supply_date, $tax_base_czk, $vat_czk, $currency, $foreign_amount, $exchange_rate, $vat_rate, $partial_deduction, $note)
              returning id
              """
            : """
              update invoice_lines set period_id=$period_id, kind=$kind, counterparty_id=$counterparty_id, counterparty_name=$counterparty_name, counterparty_dic=$counterparty_dic,
                  evidence_number=$evidence_number, taxable_supply_date=$taxable_supply_date, tax_base_czk=$tax_base_czk, vat_czk=$vat_czk,
                  currency=$currency, foreign_amount=$foreign_amount, exchange_rate=$exchange_rate, vat_rate=$vat_rate, partial_deduction=$partial_deduction, note=$note
              where id=$id
              returning id
              """;
        Add(command, "$id", invoice.Id);
        Add(command, "$period_id", invoice.PeriodId);
        Add(command, "$kind", invoice.Kind.ToString());
        Add(command, "$counterparty_id", invoice.CounterpartyId);
        Add(command, "$counterparty_name", invoice.CounterpartyName);
        Add(command, "$counterparty_dic", invoice.CounterpartyDic);
        Add(command, "$evidence_number", invoice.EvidenceNumber);
        Add(command, "$taxable_supply_date", invoice.TaxableSupplyDate.ToString("yyyy-MM-dd"));
        Add(command, "$tax_base_czk", invoice.TaxBaseCzk.ToString(CultureInfo.InvariantCulture));
        Add(command, "$vat_czk", invoice.VatCzk.ToString(CultureInfo.InvariantCulture));
        Add(command, "$currency", invoice.Currency);
        Add(command, "$foreign_amount", invoice.ForeignAmount?.ToString(CultureInfo.InvariantCulture));
        Add(command, "$exchange_rate", invoice.ExchangeRate?.ToString(CultureInfo.InvariantCulture));
        Add(command, "$vat_rate", invoice.VatRate.ToString());
        Add(command, "$partial_deduction", invoice.PartialDeduction ? 1 : 0);
        Add(command, "$note", invoice.Note);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? invoice.Id);
        invoice.Id = id;
        return id;
    }

    public async Task DeleteInvoiceAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from invoice_lines where id=$id";
        Add(command, "$id", invoiceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InvoiceReferenceDuplicate?> FindDuplicateInvoiceReferenceAsync(InvoiceLine invoice, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invoice.EvidenceNumber)
            || (invoice.CounterpartyId is null
                && string.IsNullOrWhiteSpace(invoice.CounterpartyDic)
                && string.IsNullOrWhiteSpace(invoice.CounterpartyName)))
        {
            return null;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select il.id, p.year, p.month
            from invoice_lines il
            join periods p on p.id = il.period_id
            where il.id <> $id
              and lower(trim(il.evidence_number)) = lower(trim($evidence_number))
              and case when il.kind = 'IssuedDomestic' then 'Issued' else 'Received' end = $invoice_scope
              and (
                  ($counterparty_id is not null and il.counterparty_id = $counterparty_id)
                  or ($counterparty_dic is not null and lower(trim(coalesce(il.counterparty_dic, ''))) = lower(trim($counterparty_dic)))
                  or ($counterparty_id is null and $counterparty_dic is null and $counterparty_name is not null and lower(trim(il.counterparty_name)) = lower(trim($counterparty_name)))
              )
            order by p.year desc, p.month desc, il.id desc
            limit 1
            """;
        Add(command, "$id", invoice.Id);
        Add(command, "$evidence_number", invoice.EvidenceNumber);
        Add(command, "$invoice_scope", invoice.Kind.ReferenceScope());
        Add(command, "$counterparty_id", invoice.CounterpartyId);
        Add(command, "$counterparty_dic", string.IsNullOrWhiteSpace(invoice.CounterpartyDic) ? null : invoice.CounterpartyDic);
        Add(command, "$counterparty_name", string.IsNullOrWhiteSpace(invoice.CounterpartyName) ? null : invoice.CounterpartyName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new InvoiceReferenceDuplicate(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetInt32(reader.GetOrdinal("year")),
                reader.GetInt32(reader.GetOrdinal("month")))
            : null;
    }

    public async Task<List<IssuedInvoice>> LoadIssuedInvoicesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from issued_invoices order by number desc";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<IssuedInvoice>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadIssuedInvoice(reader));
        }

        return items;
    }

    public async Task<IssuedInvoice?> LoadIssuedInvoiceAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        IssuedInvoice invoice;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "select * from issued_invoices where id=$id";
            Add(command, "$id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            invoice = ReadIssuedInvoice(reader);
        }

        await using (var itemsCommand = connection.CreateCommand())
        {
            itemsCommand.CommandText = "select * from issued_invoice_items where invoice_id=$invoice_id order by sort_order, id";
            Add(itemsCommand, "$invoice_id", id);
            await using var reader = await itemsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                invoice.Items.Add(ReadIssuedInvoiceItem(reader, id));
            }
        }

        return invoice;
    }

    // Vydané faktury se zdanitelným plněním (DUZP) v daném měsíci – pro hromadné vložení do období.
    public async Task<List<IssuedInvoice>> LoadIssuedInvoicesForPeriodAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var invoices = new List<IssuedInvoice>();
        await using (var command = connection.CreateCommand())
        {
            // DUZP je uložené jako "yyyy-MM-dd", takže prefix měsíce sedí přes like.
            command.CommandText = "select * from issued_invoices where taxable_supply_date like $prefix order by number";
            Add(command, "$prefix", $"{year:D4}-{month:D2}-%");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                invoices.Add(ReadIssuedInvoice(reader));
            }
        }

        foreach (var invoice in invoices)
        {
            await using var itemsCommand = connection.CreateCommand();
            itemsCommand.CommandText = "select * from issued_invoice_items where invoice_id=$invoice_id order by sort_order, id";
            Add(itemsCommand, "$invoice_id", invoice.Id);
            await using var reader = await itemsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                invoice.Items.Add(ReadIssuedInvoiceItem(reader, invoice.Id));
            }
        }

        return invoices;
    }

    private static IssuedInvoiceItem ReadIssuedInvoiceItem(SqliteDataReader reader, long invoiceId) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        InvoiceId = invoiceId,
        Description = Text(reader, "description"),
        Quantity = ParseDecimal(Text(reader, "quantity")),
        Unit = Text(reader, "unit"),
        UnitPriceCzk = ParseDecimal(Text(reader, "unit_price_czk")),
        VatRate = Enum.Parse<VatRateKind>(Text(reader, "vat_rate")),
        SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order"))
    };

    public async Task<long> SaveIssuedInvoiceAsync(IssuedInvoice invoice, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = invoice.Id == 0
                ? """
                  insert into issued_invoices (number, issue_date, taxable_supply_date, due_date, customer_id, customer_name, customer_ico, customer_dic, customer_street, customer_house_number, customer_city, customer_postal_code, customer_country, currency, variable_symbol, payment_method, intro_text, note, footer, created_at)
                  values ($number, $issue_date, $taxable_supply_date, $due_date, $customer_id, $customer_name, $customer_ico, $customer_dic, $customer_street, $customer_house_number, $customer_city, $customer_postal_code, $customer_country, $currency, $variable_symbol, $payment_method, $intro_text, $note, $footer, $created_at)
                  returning id
                  """
                : """
                  update issued_invoices set number=$number, issue_date=$issue_date, taxable_supply_date=$taxable_supply_date, due_date=$due_date,
                      customer_id=$customer_id, customer_name=$customer_name, customer_ico=$customer_ico, customer_dic=$customer_dic,
                      customer_street=$customer_street, customer_house_number=$customer_house_number, customer_city=$customer_city,
                      customer_postal_code=$customer_postal_code, customer_country=$customer_country, currency=$currency,
                      variable_symbol=$variable_symbol, payment_method=$payment_method, intro_text=$intro_text, note=$note, footer=$footer
                  where id=$id
                  returning id
                  """;
            Add(command, "$id", invoice.Id == 0 ? null : invoice.Id);
            Add(command, "$number", invoice.Number);
            Add(command, "$issue_date", invoice.IssueDate.ToString("yyyy-MM-dd"));
            Add(command, "$taxable_supply_date", invoice.TaxableSupplyDate.ToString("yyyy-MM-dd"));
            Add(command, "$due_date", invoice.DueDate.ToString("yyyy-MM-dd"));
            Add(command, "$customer_id", invoice.CustomerId);
            Add(command, "$customer_name", invoice.CustomerName);
            Add(command, "$customer_ico", invoice.CustomerIco);
            Add(command, "$customer_dic", invoice.CustomerDic);
            Add(command, "$customer_street", invoice.CustomerStreet);
            Add(command, "$customer_house_number", invoice.CustomerHouseNumber);
            Add(command, "$customer_city", invoice.CustomerCity);
            Add(command, "$customer_postal_code", invoice.CustomerPostalCode);
            Add(command, "$customer_country", invoice.CustomerCountry);
            Add(command, "$currency", invoice.Currency);
            Add(command, "$variable_symbol", invoice.VariableSymbol);
            Add(command, "$payment_method", invoice.PaymentMethod);
            Add(command, "$intro_text", invoice.IntroText);
            Add(command, "$note", invoice.Note);
            Add(command, "$footer", invoice.Footer);
            Add(command, "$created_at", DateTimeOffset.UtcNow.ToString("O"));
            invoice.Id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? invoice.Id);
        }

        // Položky ukládáme jako smaž-a-vlož (jednodušší než diff a v transakci bezpečné).
        await using (var deleteItems = connection.CreateCommand())
        {
            deleteItems.Transaction = (SqliteTransaction)transaction;
            deleteItems.CommandText = "delete from issued_invoice_items where invoice_id=$invoice_id";
            Add(deleteItems, "$invoice_id", invoice.Id);
            await deleteItems.ExecuteNonQueryAsync(cancellationToken);
        }

        var sortOrder = 0;
        foreach (var item in invoice.Items)
        {
            await using var insertItem = connection.CreateCommand();
            insertItem.Transaction = (SqliteTransaction)transaction;
            insertItem.CommandText = """
                insert into issued_invoice_items (invoice_id, description, quantity, unit, unit_price_czk, vat_rate, sort_order)
                values ($invoice_id, $description, $quantity, $unit, $unit_price_czk, $vat_rate, $sort_order)
                """;
            Add(insertItem, "$invoice_id", invoice.Id);
            Add(insertItem, "$description", item.Description);
            Add(insertItem, "$quantity", item.Quantity.ToString(CultureInfo.InvariantCulture));
            Add(insertItem, "$unit", item.Unit);
            Add(insertItem, "$unit_price_czk", item.UnitPriceCzk.ToString(CultureInfo.InvariantCulture));
            Add(insertItem, "$vat_rate", item.VatRate.ToString());
            Add(insertItem, "$sort_order", sortOrder++);
            await insertItem.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return invoice.Id;
    }

    public async Task DeleteIssuedInvoiceAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteItems = connection.CreateCommand())
        {
            deleteItems.Transaction = (SqliteTransaction)transaction;
            deleteItems.CommandText = "delete from issued_invoice_items where invoice_id=$id";
            Add(deleteItems, "$id", id);
            await deleteItems.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteInvoice = connection.CreateCommand())
        {
            deleteInvoice.Transaction = (SqliteTransaction)transaction;
            deleteInvoice.CommandText = "delete from issued_invoices where id=$id";
            Add(deleteInvoice, "$id", id);
            await deleteInvoice.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Další číslo faktury pro daný rok: RRRR#### (pořadí 4 místa, reset s rokem, první = RRRR0001).
    public async Task<string> NextInvoiceNumberAsync(int year, CancellationToken cancellationToken = default)
    {
        var prefix = year.ToString("D4", CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select number from issued_invoices where number like $prefix";
        Add(command, "$prefix", prefix + "%");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var maxSequence = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var number = reader.GetString(0);
            if (number.Length > prefix.Length
                && int.TryParse(number[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence))
            {
                maxSequence = Math.Max(maxSequence, sequence);
            }
        }

        return $"{prefix}{maxSequence + 1:D4}";
    }

    private static IssuedInvoice ReadIssuedInvoice(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        Number = Text(reader, "number"),
        IssueDate = DateOnly.Parse(Text(reader, "issue_date"), CultureInfo.InvariantCulture),
        TaxableSupplyDate = DateOnly.Parse(Text(reader, "taxable_supply_date"), CultureInfo.InvariantCulture),
        DueDate = DateOnly.Parse(Text(reader, "due_date"), CultureInfo.InvariantCulture),
        CustomerId = NullableLong(reader, "customer_id"),
        CustomerName = Text(reader, "customer_name"),
        CustomerIco = NullableText(reader, "customer_ico"),
        CustomerDic = NullableText(reader, "customer_dic"),
        CustomerStreet = NullableText(reader, "customer_street"),
        CustomerHouseNumber = NullableText(reader, "customer_house_number"),
        CustomerCity = NullableText(reader, "customer_city"),
        CustomerPostalCode = NullableText(reader, "customer_postal_code"),
        CustomerCountry = Text(reader, "customer_country"),
        Currency = Text(reader, "currency"),
        VariableSymbol = NullableText(reader, "variable_symbol"),
        PaymentMethod = NullableText(reader, "payment_method"),
        IntroText = NullableText(reader, "intro_text"),
        Note = NullableText(reader, "note"),
        Footer = NullableText(reader, "footer")
    };

    public async Task<AresSubject?> LoadAresCacheAsync(string ico, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from ares_cache where ico=$ico";
        Add(command, "$ico", ico);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var updated = NullableText(reader, "updated_on");
        return new AresSubject(Text(reader, "ico"), Text(reader, "official_name"), NullableText(reader, "dic"),
            updated is null ? null : DateOnly.Parse(updated));
    }

    public async Task SaveAresCacheAsync(AresSubject subject, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into ares_cache (ico, official_name, dic, updated_on, fetched_at)
            values ($ico, $official_name, $dic, $updated_on, $fetched_at)
            on conflict(ico) do update set official_name=excluded.official_name, dic=excluded.dic, updated_on=excluded.updated_on, fetched_at=excluded.fetched_at
            """;
        Add(command, "$ico", subject.Ico);
        Add(command, "$official_name", subject.OfficialName);
        Add(command, "$dic", subject.Dic);
        Add(command, "$updated_on", subject.UpdatedOn?.ToString("yyyy-MM-dd"));
        Add(command, "$fetched_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> LoadSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select value from app_settings where key=$key";
        Add(command, "$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into app_settings (key, value)
            values ($key, $value)
            on conflict(key) do update set value=excluded.value
            """;
        Add(command, "$key", key);
        Add(command, "$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var readCommand = connection.CreateCommand();
        readCommand.CommandText = $"pragma table_info({tableName})";
        await using (var reader = await readCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await ExecuteAsync(connection, $"alter table {tableName} add column {columnName} {columnDefinition}", cancellationToken);
    }

    private static void Add(SqliteCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Text(SqliteDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static string? NullableText(SqliteDataReader reader, string name)
        => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));

    private static long? NullableLong(SqliteDataReader reader, string name)
        => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(reader.GetOrdinal(name));

    private static decimal? NullableDecimal(SqliteDataReader reader, string name)
        => reader.IsDBNull(reader.GetOrdinal(name)) ? null : ParseDecimal(reader.GetString(reader.GetOrdinal(name)));

    // Tolerant of legacy rows persisted under a comma-decimal culture (e.g. cs-CZ "1234,56").
    // decimal.ToString never emits group separators, so values carry at most one decimal mark.
    private static decimal ParseDecimal(string value)
        => decimal.Parse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static bool Bool(SqliteDataReader reader, string name)
        => !reader.IsDBNull(reader.GetOrdinal(name)) && reader.GetInt64(reader.GetOrdinal(name)) != 0;

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
}

public sealed record InvoiceReferenceDuplicate(long InvoiceId, int Year, int Month)
{
    public string PeriodLabel => $"{Month:D2}/{Year:D4}";
}
