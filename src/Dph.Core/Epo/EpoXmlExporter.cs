using System.Globalization;
using System.Xml.Linq;
using Dph.Core.Calculations;
using Dph.Core.Domain;

namespace Dph.Core.Epo;

public sealed class EpoXmlExporter(EpoTaxFormDefinition? definition = null)
{
    private readonly EpoTaxFormDefinition _definition = definition ?? EpoTaxFormDefinition.Current;
    private readonly VatCalculator _calculator = new();

    // lastKnownReturns: dříve podaná přiznání období – naposledy podané řádné/opravné DP a za ním
    // případná už podaná dodatečná DP (jejich rozdíly se přičtou). Povinné pro dodatečné přiznání
    // (forma D/E), které se dle pokynů k DPHDP3 vyplňuje jen rozdílově oproti poslední známé dani.
    public XDocument ExportVatReturn(TaxSubject subject, VatPeriod period, IEnumerable<InvoiceLine> invoices, string? formType = null, IReadOnlyCollection<XDocument>? lastKnownReturns = null)
    {
        var resolvedFormType = formType ?? period.FormType;
        var supplementary = resolvedFormType is "D" or "E";
        if (supplementary && (lastKnownReturns is null || lastKnownReturns.Count == 0))
        {
            throw new ArgumentException("Dodatečné přiznání (forma D/E) vyžaduje naposledy podané přiznání pro výpočet rozdílů.", nameof(lastKnownReturns));
        }

        var lines = invoices.ToArray();
        var dph = new XElement(_definition.VatReturnElement,
            new XAttribute("verzePis", _definition.VatReturnVersion),
            VatReturnHeader(subject, period, resolvedFormType),
            TaxSubjectElement(subject));

        // InvoiceKind.ReverseCharge = přijetí služby ze zahraničí, kde daň přiznává příjemce. Řádek
        // se liší podle původu dodavatele (rozpoznáno z prefixu jeho DIČ):
        //   - dodavatel registrovaný v JČS (EU prefix, např. IE u OpenAI Ireland) → §9(1) → ř.5/6
        //     (Veta1 p_sl23_e / p_sl5_e),
        //   - dodavatel ze třetí země / bez EU DIČ (např. GitHub, Anthropic z USA) → §108 → ř.12/13
        //     (Veta1 p_sl23_z / p_sl5_z).
        // Nárok na odpočet u obojího jde na ř.43/44 (Veta4 nar_zdp / od_zdp). NENÍ to tuzemský režim
        // přenesení §92a (ř.10/11 + KH B1); v kontrolním hlášení se tato plnění vykazují v oddílu A.2.
        var buckets = ComputeBuckets(lines);

        // V dodatečném přiznání se uvádí pouze rozdíly od údajů, ze kterých byla stanovena poslední
        // známá daň (pokyny k DPHDP3). Odečítají se hodnoty skutečně vykázané v podaném XML – ne
        // rekonstrukce z aktuální logiky výpočtu, aby rozdíl seděl i proti podáním ze starších verzí
        // aplikace; jejich řádky, které dnes už negenerujeme, se vynulují zápornou hodnotou.
        var previous = supplementary ? SumReportedValues(lastKnownReturns!) : new Dictionary<(string, string), long>();
        var consumed = new HashSet<(string Element, string Attribute)>();
        long Previous(string element, string attribute)
        {
            consumed.Add((element, attribute));
            return previous.GetValueOrDefault((element, attribute));
        }

        void AddDiffRow(XElement element, string baseAttr, string vatAttr, (long Base, long Vat) current)
            => AddRow(element, baseAttr, vatAttr, (
                current.Base - Previous(element.Name.LocalName, baseAttr),
                current.Vat - Previous(element.Name.LocalName, vatAttr)));

        // Tuzemská plnění dělíme podle sazby na základní (ř.1/40) a sníženou (ř.2/41). Každý řádek
        // se do XML zapisuje vždy jako dvojice základ+daň – EPO jinak hlásí „je zadána jen jedna
        // z hodnot“ (kód 48).
        var veta1 = new XElement("Veta1");
        AddDiffRow(veta1, "obrat23", "dan23", buckets.OutStd);          // ř.1  tuzemsko, základní sazba
        AddDiffRow(veta1, "obrat5", "dan5", buckets.OutRed);            // ř.2  tuzemsko, snížená sazba
        AddDiffRow(veta1, "p_sl23_e", "dan_psl23_e", buckets.EuStd);    // ř.5  EU služba, základní sazba
        AddDiffRow(veta1, "p_sl5_e", "dan_psl5_e", buckets.EuRed);      // ř.6  EU služba, snížená sazba
        AddDiffRow(veta1, "p_sl23_z", "dan_psl23_z", buckets.NonEuStd); // ř.12 třetí země, základní sazba
        AddDiffRow(veta1, "p_sl5_z", "dan_psl5_z", buckets.NonEuRed);   // ř.13 třetí země, snížená sazba

        // ř.43/44 odpočet sčítá EU i třetí zemi podle sazby.
        var veta4 = new XElement("Veta4");
        AddDiffRow(veta4, "pln23", "odp_tuz23_nar", buckets.InStd);     // ř.40 odpočet z tuzemských plnění, základní sazba
        AddDiffRow(veta4, "pln5", "odp_tuz5_nar", buckets.InRed);       // ř.41 odpočet z tuzemských plnění, snížená sazba
        AddDiffRow(veta4, "nar_zdp23", "od_zdp23", (buckets.StdDeductBase, buckets.StdDeductVat)); // ř.43 odpočet reverse charge, základní sazba
        AddDiffRow(veta4, "nar_zdp5", "od_zdp5", (buckets.RedDeductBase, buckets.RedDeductVat));   // ř.44 odpočet reverse charge, snížená sazba

        // Souhrny ř.62/63 se odečítají proti podaným součtům, ne po řádcích – kdyby staré podání
        // mělo plnění na řádku, který už negenerujeme, rozdíl daně musí přesto sedět.
        var taxDue = buckets.TaxDueWhole - Previous("Veta6", "dan_zocelk");
        var taxDeduction = buckets.TaxDeductionWhole - Previous("Veta6", "odp_zocelk");

        // Řádky starších verzí, které aktuální logika negeneruje (jinak by v dodatečném přiznání
        // zůstaly stát v poslední známé dani), se vynulují zápornou hodnotou.
        var veta2 = new XElement("Veta2");
        foreach (var ((element, attribute), value) in previous)
        {
            if (value == 0 || consumed.Contains((element, attribute)))
            {
                continue;
            }

            var target = element switch
            {
                "Veta1" => veta1,
                "Veta2" => veta2,
                "Veta4" => veta4,
                _ => null
            };
            target?.Add(A(attribute, -value));
        }

        if (veta1.HasAttributes)
        {
            dph.Add(veta1);
        }

        if (veta2.HasAttributes)
        {
            dph.Add(veta2);
        }

        if (veta4.HasAttributes)
        {
            // ř.46 „V plné výši“ = součet odpočtů; musí sednout se součtem výše uvedených řádků.
            veta4.Add(A("odp_sum_nar", taxDeduction));
            dph.Add(veta4);
        }

        if (supplementary)
        {
            // Dodatečné přiznání: ř.62/63 jsou rozdíly a ř.66 „Rozdíl oproti poslední známé dani“
            // (kladný i záporný); ř.64/65 se vyplňují jen v řádném/opravném přiznání.
            dph.Add(new XElement("Veta6",
                A("dan_zocelk", taxDue),
                A("odp_zocelk", taxDeduction),
                A("dano", taxDue - taxDeduction)));
        }
        else
        {
            // ř.62/63/64: počítáme ze zaokrouhlených řádků, aby ř.64 = ř.62 - ř.63 přesně sedělo
            // (EPO si tyto součty přepočítává a jinak by podání odmítlo).
            dph.Add(new XElement("Veta6",
                A("dan_zocelk", buckets.TaxDueWhole),
                A("odp_zocelk", buckets.TaxDeductionWhole),
                A(buckets.NetTaxWhole >= 0 ? "dano_da" : "dano_no", Math.Abs(buckets.NetTaxWhole)),
                A(buckets.NetTaxWhole >= 0 ? "dano_no" : "dano_da", 0),
                A("dano", 0)));
        }

        return Wrap(dph);
    }

    // Hodnoty vykázané v dříve podaných přiznáních: naposledy podané řádné/opravné plus rozdíly
    // z případných pozdějších dodatečných = poslední známá daň po jednotlivých polích XML.
    private static Dictionary<(string Element, string Attribute), long> SumReportedValues(IReadOnlyCollection<XDocument> returns)
    {
        var result = new Dictionary<(string, string), long>();
        foreach (var document in returns)
        {
            foreach (var element in document.Descendants())
            {
                var name = element.Name.LocalName;
                if (name is not ("Veta1" or "Veta2" or "Veta4" or "Veta6"))
                {
                    continue;
                }

                foreach (var attribute in element.Attributes())
                {
                    // ř.64/65 (dano_da/dano_no) a ř.66 (dano) nejsou vykázaná plnění, ale výsledek;
                    // odp_sum_nar (ř.46) je jen kontrolní součet ř.43/44 – rozdíl se počítá z nich.
                    if (name == "Veta6" && attribute.Name.LocalName is not ("dan_zocelk" or "odp_zocelk")
                        || attribute.Name.LocalName == "odp_sum_nar")
                    {
                        continue;
                    }

                    if (long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        var key = (name, attribute.Name.LocalName);
                        result[key] = result.GetValueOrDefault(key) + value;
                    }
                }
            }
        }

        return result;
    }

    // Vlastní daňová povinnost v celých korunách (ř.64 přiznání): > 0 = doplatek, < 0 = nadměrný
    // odpočet. Počítá se přesně jako v DP XML (po řádcích zaokrouhleno na koruny), což je částka,
    // která se reálně platí – ne haléřový součet z výpočtu.
    public long ComputeNetTaxWholeCrowns(IEnumerable<InvoiceLine> invoices)
        => ComputeBuckets(invoices.ToArray()).NetTaxWhole;

    private ReturnBuckets ComputeBuckets(InvoiceLine[] lines)
    {
        var rc = lines.Where(x => x.Kind == InvoiceKind.ReverseCharge).ToArray();
        return new ReturnBuckets(
            DomesticLine(lines, InvoiceKind.IssuedDomestic, reduced: false),
            DomesticLine(lines, InvoiceKind.IssuedDomestic, reduced: true),
            DomesticLine(lines, InvoiceKind.ReceivedDomesticWithVat, reduced: false),
            DomesticLine(lines, InvoiceKind.ReceivedDomesticWithVat, reduced: true),
            RcLine(rc, eu: true, reduced: false),
            RcLine(rc, eu: true, reduced: true),
            RcLine(rc, eu: false, reduced: false),
            RcLine(rc, eu: false, reduced: true));
    }

    // Zaokrouhlené (celé koruny) řádky přiznání. Reverse charge se objevuje na výstupu i v odpočtu,
    // takže se ve výsledné dani vyruší.
    private readonly record struct ReturnBuckets(
        (long Base, long Vat) OutStd,
        (long Base, long Vat) OutRed,
        (long Base, long Vat) InStd,
        (long Base, long Vat) InRed,
        (long Base, long Vat) EuStd,
        (long Base, long Vat) EuRed,
        (long Base, long Vat) NonEuStd,
        (long Base, long Vat) NonEuRed)
    {
        public long StdDeductBase => EuStd.Base + NonEuStd.Base;
        public long RedDeductBase => EuRed.Base + NonEuRed.Base;
        // Odpočet ř.43/44 = daň skutečně přiznaná na výstupu na ř.5/6/12/13, tedy SOUČET daní
        // jednotlivých řádků – ne round(sečtený_základ × sazba). Kdyby se daň dopočítávala znovu
        // ze sloučeného základu (412+208=620 → round(130,2)=130), lišila by se o korunu od výstupu
        // (87+44=131) a reverse charge by se ve vlastní dani nevyrušil (ř.66 by nesedělo o 1 Kč).
        public long StdDeductVat => EuStd.Vat + NonEuStd.Vat;
        public long RedDeductVat => EuRed.Vat + NonEuRed.Vat;
        public long TaxDueWhole => OutStd.Vat + OutRed.Vat + EuStd.Vat + EuRed.Vat + NonEuStd.Vat + NonEuRed.Vat;
        public long TaxDeductionWhole => InStd.Vat + InRed.Vat + StdDeductVat + RedDeductVat;
        public long NetTaxWhole => TaxDueWhole - TaxDeductionWhole;
    }

    // Zapíše řádek přiznání jako dvojici základ+daň, a to jen pokud je aspoň jedna hodnota nenulová.
    private static void AddRow(XElement element, string baseAttr, string vatAttr, (long Base, long Vat) row)
    {
        if (row.Base != 0 || row.Vat != 0)
        {
            element.Add(A(baseAttr, row.Base), A(vatAttr, row.Vat));
        }
    }

    // Whole-crown (base, vat) pro tuzemská plnění daného druhu a sazby (snížená 12 % vs. ostatní).
    private (long Base, long Vat) DomesticLine(InvoiceLine[] lines, InvoiceKind kind, bool reduced)
    {
        var bucket = lines
            .Where(x => x.Kind == kind && (x.VatRate == VatRateKind.Reduced12) == reduced)
            .ToArray();
        return (
            VatCalculator.WholeCrowns(VatCalculator.Money(bucket.Sum(x => x.TaxBaseCzk))),
            VatCalculator.WholeCrowns(VatCalculator.Money(bucket.Sum(_calculator.ResolveVat))));
    }

    private static long ReverseChargeVatForReportedBase(long reportedBase, InvoiceLine[] bucket)
    {
        if (bucket.Length == 0)
        {
            return 0;
        }

        var rates = bucket.Select(x => x.VatRate).Distinct().ToArray();
        if (rates.Length == 1)
        {
            return TaxFromReportedBase(reportedBase, rates[0]);
        }

        return VatCalculator.WholeCrowns(VatCalculator.Money(bucket
            .GroupBy(x => x.VatRate)
            .Sum(group =>
            {
                var groupBase = VatCalculator.WholeCrowns(VatCalculator.Money(group.Sum(x => x.TaxBaseCzk)));
                return groupBase * VatCalculator.Rate(group.Key);
            })));
    }

    private static long TaxFromReportedBase(long reportedBase, VatRateKind rate)
        => VatCalculator.WholeCrowns(VatCalculator.Money(reportedBase * VatCalculator.Rate(rate)));

    // Whole-crown (base, vat) for one reverse-charge bucket. eu = dodavatel registrovaný v JČS
    // (EU prefix DIČ), reduced = snížená sazba (12 %); ostatní (vč. 0 %) spadá do základní.
    private (long Base, long Vat) RcLine(InvoiceLine[] reverseCharge, bool eu, bool reduced)
    {
        var bucket = reverseCharge
            .Where(x => IsEuSupplier(x) == eu && (x.VatRate == VatRateKind.Reduced12) == reduced)
            .ToArray();
        var reportedBase = VatCalculator.WholeCrowns(VatCalculator.Money(bucket.Sum(x => x.TaxBaseCzk)));
        return (reportedBase, ReverseChargeVatForReportedBase(reportedBase, bucket));
    }

    private static bool IsEuSupplier(InvoiceLine invoice)
        => InvoiceKindClassifier.IsEuSupplier(invoice.CounterpartyDic);

    public XDocument ExportControlStatement(TaxSubject subject, VatPeriod period, IEnumerable<InvoiceLine> invoices, string? formType = null)
    {
        var lines = invoices.ToArray();
        var dph = new XElement(_definition.ControlStatementElement,
            new XAttribute("verzePis", _definition.ControlStatementVersion),
            ControlStatementHeader(period, formType ?? period.FormType),
            TaxSubjectElement(subject, includeDataBox: true));

        // A.2 – přijatá plnění, u kterých přiznává daň příjemce (ř. 3, 4, 5, 6, 9, 12 a 13 DP),
        // tedy i zde modelovaný reverse charge (přijetí služby ze zahraničí, ř.5/6 a ř.12/13).
        // Uvádí se po dokladech bez hodnotového limitu; VAT ID dodavatele se rozděluje na kód
        // státu a číslo a vyplňuje se jen u dodavatele registrovaného v EU.
        var row = 1;
        foreach (var document in GroupByDocument(lines, InvoiceKind.ReverseCharge))
        {
            var (state, vatId) = SplitEuVatId(document.Dic);
            var element = new XElement("VetaA2",
                A("c_radku", row++),
                A("k_stat", state),
                A("vatid_dod", vatId),
                A("c_evid_dd", document.EvidenceNumber),
                A("dppd", Date(document.TaxableSupplyDate)));
            AddRateColumns(element, document.Lines);
            dph.Add(element);
        }

        // A.4/A.5 a B.2/B.3: limit 10 000 Kč vč. daně platí pro celý doklad, proto se řádky
        // tabulky (jeden na sazbu) nejdřív seskupí podle dokladu; sazby jdou do sloupců
        // zakl_dane1/dan1 (základní) a zakl_dane2/dan2 (snížená).
        var issuedDocuments = GroupByDocument(lines, InvoiceKind.IssuedDomestic);
        row = 1;
        foreach (var document in issuedDocuments.Where(x => IsDetail(x, "A5")))
        {
            var element = new XElement("VetaA4",
                A("c_radku", row++),
                A("c_evid_dd", document.EvidenceNumber),
                A("dppd", Date(document.TaxableSupplyDate)),
                A("dic_odb", StripCz(document.Dic)));
            AddRateColumns(element, document.Lines);
            element.Add(A("kod_rezim_pl", "0"), A("zdph_44", "N"));
            dph.Add(element);
        }

        AddSummarySection(dph, "VetaA5", issuedDocuments.Where(x => !IsDetail(x, "A5")));

        var receivedDocuments = GroupByDocument(lines, InvoiceKind.ReceivedDomesticWithVat);
        row = 1;
        foreach (var document in receivedDocuments.Where(x => IsDetail(x, "B3")))
        {
            var element = new XElement("VetaB2",
                A("c_radku", row++),
                A("c_evid_dd", document.EvidenceNumber),
                A("dppd", Date(document.TaxableSupplyDate)),
                A("dic_dod", StripCz(document.Dic)));
            AddRateColumns(element, document.Lines);
            element.Add(
                A("pomer", document.Lines.Any(x => x.PartialDeduction) ? "A" : "N"),
                A("zdph_44", "N"));
            dph.Add(element);
        }

        AddSummarySection(dph, "VetaB3", receivedDocuments.Where(x => !IsDetail(x, "B3")));

        // Kontrolní součty oddílů; EPO je porovnává s řádky přiznání (ř.1/2, ř.40/41, ř.5+6+12+13),
        // proto musí sedět dělení podle sazeb i součet základů A.2.
        var issuedLines = lines.Where(x => x.Kind == InvoiceKind.IssuedDomestic).ToArray();
        var receivedLines = lines.Where(x => x.Kind == InvoiceKind.ReceivedDomesticWithVat).ToArray();
        dph.Add(new XElement("VetaC",
            A("obrat23", Money(SumBase(issuedLines, reduced: false))),
            A("obrat5", Money(SumBase(issuedLines, reduced: true))),
            A("pln23", Money(SumBase(receivedLines, reduced: false))),
            A("pln5", Money(SumBase(receivedLines, reduced: true))),
            A("celk_zd_a2", Money(lines.Where(x => x.Kind == InvoiceKind.ReverseCharge).Sum(x => x.TaxBaseCzk)))));

        return Wrap(dph);
    }

    // Jeden doklad KH = shodné evidenční číslo + protistrana. Faktura s více sazbami je v tabulce
    // jako víc řádků (jeden na sazbu) a v KH musí tvořit jediný řádek se sloupci podle sazeb;
    // řádky bez evidenčního čísla se neslučují (nejde poznat, že patří k sobě).
    private static List<ControlStatementDocument> GroupByDocument(InvoiceLine[] lines, InvoiceKind kind)
        => lines
            .Where(x => x.Kind == kind)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.EvidenceNumber)
                ? $"#{x.Id}"
                : $"{x.EvidenceNumber.Trim().ToUpperInvariant()}|{x.CounterpartyDic?.Trim().ToUpperInvariant()}")
            .Select(g => new ControlStatementDocument(
                g.First().EvidenceNumber,
                g.First().CounterpartyDic,
                g.Min(x => x.TaxableSupplyDate),
                [.. g]))
            .ToList();

    private sealed record ControlStatementDocument(
        string EvidenceNumber,
        string? Dic,
        DateOnly TaxableSupplyDate,
        IReadOnlyList<InvoiceLine> Lines)
    {
        public decimal GrossCzk => Lines.Sum(x => x.GrossCzk);
    }

    private bool IsDetail(ControlStatementDocument document, string summaryCode)
        => !string.Equals(document.EvidenceNumber, summaryCode, StringComparison.OrdinalIgnoreCase)
           && document.GrossCzk > _definition.ControlStatementDetailLimitCzk;

    // Souhrnný řádek (A.5/B.3) za doklady pod limitem a za importované souhrny.
    private void AddSummarySection(XElement dph, string elementName, IEnumerable<ControlStatementDocument> documents)
    {
        var lines = documents.SelectMany(x => x.Lines).ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        var element = new XElement(elementName);
        AddRateColumns(element, lines);
        dph.Add(element);
    }

    // Sloupce KH podle sazby: 1 = základní (vč. 0 %), 2 = snížená 12 % – stejné dělení jako
    // ř.1/ř.2 přiznání, aby křížová kontrola DP ↔ KH seděla.
    private void AddRateColumns(XElement element, IEnumerable<InvoiceLine> lines)
    {
        var byRate = lines.ToLookup(x => x.VatRate == VatRateKind.Reduced12);
        AddRatePair(element, "zakl_dane1", "dan1", [.. byRate[false]]);
        AddRatePair(element, "zakl_dane2", "dan2", [.. byRate[true]]);
    }

    private void AddRatePair(XElement element, string baseAttr, string vatAttr, InvoiceLine[] lines)
    {
        if (lines.Length == 0)
        {
            return;
        }

        element.Add(
            A(baseAttr, Money(lines.Sum(x => x.TaxBaseCzk))),
            A(vatAttr, Money(lines.Sum(_calculator.ResolveVat))));
    }

    private static decimal SumBase(IEnumerable<InvoiceLine> lines, bool reduced)
        => lines.Where(x => (x.VatRate == VatRateKind.Reduced12) == reduced).Sum(x => x.TaxBaseCzk);

    // VAT ID dodavatele pro A.2 rozdělené na kód státu a číslo. Dodavatel ze třetí země bez EU
    // registrace nechává obě pole prázdná.
    private static (string State, string Number) SplitEuVatId(string? dic)
    {
        var trimmed = dic?.Trim();
        return trimmed is { Length: > 2 } && InvoiceKindClassifier.IsEuSupplier(trimmed)
            ? (trimmed[..2].ToUpperInvariant(), trimmed[2..])
            : ("", "");
    }

    private XElement VatReturnHeader(TaxSubject subject, VatPeriod period, string formType)
    {
        var element = new XElement("VetaD",
            A("dokument", "DP3"),
            A("k_uladis", "DPH"),
            A("dapdph_forma", formType),
            A("mesic", period.Month.ToString("D2", CultureInfo.InvariantCulture)),
            A("rok", period.Year),
            A("d_poddp", Date(period.SubmissionDate)),
            A("typ_platce", "P"),
            A("c_okec", subject.ActivityCode));

        // Dodatečné přiznání musí uvést den zjištění důvodů pro jeho podání (§141 odst. 1 DŘ).
        if (formType is "D" or "E")
        {
            element.Add(A("d_zjist", Date(period.SubmissionDate)));
        }

        return element;
    }

    private XElement ControlStatementHeader(VatPeriod period, string formType)
    {
        var element = new XElement("VetaD",
            A("dokument", "KH1"),
            A("k_uladis", "DPH"),
            A("khdph_forma", formType),
            A("mesic", period.Month),
            A("rok", period.Year),
            A("d_poddp", Date(period.SubmissionDate)));

        // Následné KH musí uvést den zjištění důvodů pro jeho podání (§101f odst. 2 ZDPH).
        if (formType is "N" or "E")
        {
            element.Add(A("d_zjist", Date(period.SubmissionDate)));
        }

        return element;
    }

    private XElement TaxSubjectElement(TaxSubject subject, bool includeDataBox = false)
    {
        // „ulice“ musí odpovídat registračním údajům u FÚ, jinak EPO hlásí kód 116. Obec bez
        // uliční sítě (adresa jen „obec + č.p.“) má v registraci ulici prázdnou – pak má být
        // prázdné i pole Ulice v nastavení subjektu; exportér data záměrně nijak neupravuje.
        var element = new XElement("VetaP",
            A("dic", StripCz(subject.Dic)),
            A("jmeno", subject.FirstName),
            A("prijmeni", subject.LastName),
            A("ulice", subject.Street),
            A("c_pop", subject.HouseNumber),
            A("naz_obce", subject.City),
            A("psc", subject.PostalCode),
            A("stat", subject.Country),
            A("email", subject.Email),
            A("c_telef", subject.Phone),
            A("c_ufo", subject.TaxOfficeCode),
            A("c_pracufo", subject.WorkplaceCode),
            A("typ_ds", "F"));

        if (!string.IsNullOrWhiteSpace(subject.Title))
        {
            element.Add(A("titul", subject.Title));
        }

        if (includeDataBox && !string.IsNullOrWhiteSpace(subject.DataBoxId))
        {
            element.Add(A("id_dats", subject.DataBoxId));
        }

        return element;
    }

    private XDocument Wrap(XElement form) => new(new XDeclaration("1.0", "UTF-8", null),
        new XElement("Pisemnost",
            A("nazevSW", _definition.SoftwareName),
            A("verzeSW", _definition.SoftwareVersion),
            form));

    private static XAttribute A(string name, object? value) => new(name, value ?? "");
    private static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => VatCalculator.Money(value).ToString("0.##", CultureInfo.InvariantCulture);
    private static string StripCz(string? value) => string.IsNullOrWhiteSpace(value)
        ? ""
        : value.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
}
