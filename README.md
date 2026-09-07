# DPH Asistent

DPH Asistent je desktopová aplikace pro evidenci podkladů k českému přiznání k DPH, kontrolnímu hlášení a vystavování faktur. Je napsaná v .NET/Avalonia a data ukládá lokálně do SQLite databáze.

> Aplikace pomáhá s evidencí a generováním XML pro EPO, ale nenahrazuje účetní, daňového poradce ani kontrolu v portálu EPO před podáním.

## Instalace

- **Windows** – stáhněte instalátor `DphAsistent-Setup-x.y.z.exe` ze [stránky Releases](https://github.com/KoudelkaB/DPH-Asistent/releases), nebo (po publikaci) `winget install BohdanKoudelka.DPHAsistent`.
- **Linux** – nainstalujte přiložený `DphAsistent-x.y.z.flatpak` (`flatpak install ./DphAsistent-x.y.z.flatpak`), nebo (po publikaci) z Flathubu `flatpak install flathub io.github.koudelkab.DphAsistent`.
- K dispozici jsou i přenosné archivy (`*-portable.zip` / `*-portable.tar.gz`) bez instalace.

Sestavení balíčků a publikace na Flathub/Winget jsou popsané v [PUBLISHING.md](PUBLISHING.md).

## Hlavní funkce

- Evidence měsíčních období DPH.
- Evidence poplatníka včetně DIČ, IČO, adresy, e-mailu, telefonu, datové schránky, finančního úřadu, územního pracoviště a bankovního účtu.
- Doplnění údajů poplatníka a subjektů z ARES.
- Načítání seznamu finančních úřadů a územních pracovišť.
- Evidence vydaných, tuzemských přijatých dokladů a zahraničních služeb v režimu reverse charge; režim se volí podle povahy plnění.
- Automatický výpočet základu, DPH a částky s DPH podle sazby.
- Nové položky nabízejí podporované sazby DPH 21 % a 12 %. Staré položky s 0 % zůstávají viditelné k opravě a existující faktury lze uložit i bez změny sazby; nové vystavování a automatické vložení takových faktur do DPH je zablokováno.
- Podpora cizí měny a dopočtu základu v CZK kurzem ČNB podle DUZP.
- Adresář odběratelů a dodavatelů s vazbou na doklady.
- Automatické ukládání řádků dokladů během práce.
- Uzamčení již podaného (importovaného/exportovaného) období s potvrzením před další úpravou.
- Rozlišení řádného, opravného a – po lhůtě – dodatečného přiznání a následného kontrolního hlášení.
- Záloha a obnova lokální databáze.

## Přiznání k DPH a kontrolní hlášení

Aplikace z evidovaných řádků generuje dvě XML dávky pro EPO:

- přiznání k DPH,
- kontrolní hlášení.

Export pracuje s těmito typy řádků:

- **Vydaná tuzemská plnění**: výstupní daň, řádky 1/2 přiznání, kontrolní hlášení A.4/A.5.
- **Přijatá tuzemská plnění s českou DPH**: odpočet, řádky 40/41 přiznání, kontrolní hlášení B.2/B.3.
- **Reverse charge pro zahraniční služby**: přijetí služby od osoby neusazené v tuzemsku. Dodavatelé registrovaní v jiném členském státě EU se exportují do řádků 5/6, dodavatelé ze třetích zemí do řádků 12/13, odpočet u obojího do řádků 43/44. V kontrolním hlášení se vykazují v oddílu A.2 (EU dodavatel s rozděleným VAT ID, třetí země s prázdnou identifikací).

Typ dokladu se volí výslovně: **Vydaná**, **Přijatá** (tuzemská s českou DPH), nebo **Zahraniční služba (RC)**. Samotné DIČ neurčuje místo plnění, povinnost přiznat daň ani nárok na odpočet. U RC se prefixem DIČ rozlišuje registrace v EU a třetí země. Severní Irsko (`XI`) je pro služby třetí zemí. Tuzemský režim přenesení daňové povinnosti podle § 92a (ř. 10/11, KH B.1) aplikace nemodeluje.

Export je určen pro měsíčního plátce – fyzickou osobu, období od roku 2024, běžná zdanitelná plnění se sazbami 21 % a 12 % a plný nebo poměrný nárok na odpočet podle § 75. Základ a DPH zadávejte již v uplatňované výši; „Poměr“ pouze nastavuje příznak `pomer` v B.2 a částky nepřepočítává. Volba „B.2“ se zobrazuje při zaškrtnutém poměru a součtu zadaných částek celého dokladu v absolutní hodnotě nejvýše 10 000 Kč včetně DPH; označuje celý původní doklad nad limitem a rozhoduje o B.2 místo B.3. „Poměr“ lze upravit bez ohledu na limit; v B.3 se tento příznak do XML nezapisuje. Odškrtnutí „Poměr“ zruší ruční zařazení do B.2; nadlimitní součet dokladu jej stále zařadí do B.2 automaticky. Import B.2 zachovává výslovné zařazení pouze u částek nepřesahujících limit. Při následné změně částek bez použití poměru se toto zařazení zruší. Krácení koeficientem podle § 76 aplikace nepočítá a „Poměr“ jej neoznačuje. Neurčená osvobození (položky 0 %) export zastaví s vysvětlením. Aplikace také automaticky neposuzuje nárok na odpočet, splatnost přijatých závazků ani opravy odpočtu u nezaplacených faktur podle § 74b. Tyto případy, historické sazby, zálohy a zvláštní režimy je nutné řešit samostatně v EPO.

Daň se vede v haléřích; export sčítá evidované částky a zaokrouhluje je na celé koruny. U reverse charge zůstává záměrná kompenzace: odpočet ř. 43/44 přebírá součet vykázaných výstupních řádků, aby RC s plným nárokem mělo nulový dopad na výslednou daň. Tento postup se může lišit o korunu od samostatného zaokrouhlení celého součtu odpočtu. Jde o zachované pravidlo aplikace, nikoli o doloženou zákonnou výjimku ze zaokrouhlování; před podáním ověřte jeho přijetí v EPO.

Před výběrem exportní složky probíhá stejná kontrola celých dokladů jako při sestavení XML. DIČ se normalizuje včetně vnitřních mezer. Limit KH se posuzuje podle celého dokladu, u běžných oprav podle absolutní hodnoty opravy. Tuzemská vydaná plnění bez českého DIČ odběratele se vykazují v A.5; u podnikajícího odběratele doplňte jeho tuzemské DIČ, pokud mu bylo přiděleno.

Při opakovaném exportu již podaného období se aplikace řídí lhůtou pro podání (25. den následujícího měsíce, posunutý na nejbližší pracovní den):

- **Do lhůty** nabídne řádné (přepíše stávající XML), nebo **opravné** přiznání a kontrolní hlášení (forma „O“).
- **Po lhůtě** vygeneruje **dodatečné přiznání** (forma „D“, jen rozdíly oproti poslední známé dani na ř. 66, se skutečným datem zjištění, které uživatel vyplní při exportu) a **následné kontrolní hlášení** (forma „N“, kompletní data). Rozdíly se počítají proti hodnotám skutečně vykázaným v naposledy podaném XML; beze změny plnění se dodatečné přiznání nepodává a vznikne jen následné kontrolní hlášení.

Opravné, dodatečné i následné exporty dostávají samostatný název souboru, aby nepřepsaly předchozí podání.

## Vydané faktury

Samostatná agenda vydaných faktur umožňuje:

- založit novou fakturu s automatickým číslem,
- použít existující fakturu jako šablonu,
- evidovat odběratele ručně, z adresáře nebo doplněním z ARES,
- zadat položky faktury s množstvím, měrnou jednotkou, cenou za jednotku a sazbou DPH,
- automaticky spočítat základ, DPH a celkovou částku,
- nastavit datum vystavení, DUZP, splatnost a variabilní symbol (vystavování a PDF podporují CZK),
- použít úvodní text s placeholdery `{měsíc}` a `{rok}`,
- přidat poznámku a patičku,
- vložit fakturu do evidence DPH,
- uložit fakturu do PDF.

PDF faktura obsahuje dodavatele, odběratele, platební údaje, QR platbu, položky, rekapitulaci DPH a částku k úhradě. Pokud není vyplněný IBAN, aplikace se ho pokusí dopočítat z českého bankovního účtu ve tvaru `[předčíslí-]číslo/kód banky`.

Výchozí název PDF má tvar:

```text
20260005 – Bohdan Koudelka – MyQ, spol. s r.o..pdf
```

## Import historických XML

Na kartě Import lze načíst složku s historickými XML soubory. Import slouží k převzetí starších dat do lokální databáze:

- načte údaje poplatníka,
- založí nalezená období,
- doplní adresář subjektů,
- importuje dokladové řádky, pokud období ještě nemá vlastní řádky; následné/opravné KH nahrazuje předchozí KH podle data vyhotovení a druhu podání,
- opakované načtení stejného KH nezvyšuje částky,
- odmítne nepodporované oddíly KH a neplatné částky; při nejednoznačné historii je nutné vybrat poslední skutečně podané KH,
- chybný KH může zablokovat pouze stejné období stejného identifikovaného poplatníka; import oznámí konkrétní období a důvod. Jiné formuláře (např. souhrnné hlášení) a soubory jiného poplatníka pouze přeskočí,
- přeskočené nebo nevyhovující soubory započítá do výsledného hlášení.

Historická XML jsou brána jako vstupní data; autoritou pro nová podání je vždy aktuální export z aplikace a následná kontrola v EPO.

## Lokální data

Data jsou uložená v uživatelském profilu:

```text
<LocalApplicationData>/DphAssistant/dph.sqlite
<LocalApplicationData>/DphAssistant/exports
```

Na Linuxu to typicky odpovídá cestě pod `~/.local/share/DphAssistant`.

## Vývoj

Projekt používá .NET 10 a Avalonia.

Sestavení aplikace:

```bash
dotnet build src/Dph.App/Dph.App.csproj
```

Spuštění aplikace:

```bash
dotnet run --project src/Dph.App/Dph.App.csproj
```

Spuštění testů:

```bash
dotnet test Dph.slnx
```

Verze aplikace se neudržuje ručně – generuje ji [MinVer](https://github.com/adamralph/minver) z git tagů `vX.Y.Z`. Postup vydání je v [PUBLISHING.md](PUBLISHING.md).

## Struktura projektu

- `src/Dph.App` - Avalonia desktopová aplikace a viewmodely.
- `src/Dph.Core` - doména, výpočty DPH, EPO XML import/export, ARES/ČNB služby, PDF faktury a persistence.
- `tests/Dph.Core.Tests` - testy výpočtů, importu/exportu, repository, ARES/ČNB pomocných služeb a PDF rendereru.

## Licence

Projekt je dostupný pod licencí MIT. Viz [LICENSE](LICENSE).

## Podklady pro daňová pravidla

Zdroje použité při revizi daňových výpočtů:

- [Pokyny Finanční správy k přiznání DPH, vzor 22](https://financnisprava.gov.cz/assets/tiskopisy/5412_22.pdf): rozdělení plnění a odpočtů do řádků, rozdílové dodatečné přiznání.
- [Metodická informace ke KH od 1. 1. 2024](https://financnisprava.gov.cz/assets/cs/prilohy/d-seznam-dani/Metodicka_informace_k_vyplneni_KH_20240101.pdf): limit dokladu, záporné opravy, identifikace protistran, částečný odpočet.
- [Informace GFŘ k Brexitu](https://financnisprava.gov.cz/assets/cs/prilohy/d-seznam-dani/Info-dopady-BREXITu-na-DPH-od-20210101.pdf): Severní Irsko v režimu služeb.
- [Výpočet a zaokrouhlování DPH](https://financnisprava.gov.cz/cs/financni-sprava/novinky/novinky-2019/vypocet-dph-a-zaokrouhlovani-od-1-10-2019): metody výpočtu daně; aktuální sazby určuje § 47 ZDPH.
- [Aktuální struktura DPHDP3](https://adisspr.mfcr.cz/dpr/adis/idpr_pub/epo2_info/popis_struktury_detail.faces?zkratka=DPHDP3): pole přiznání a skutečný den zjištění důvodů dodatečného podání.

Vygenerování XML není odeslání ani potvrzení podání. Historie exportních/importních souborů musí odpovídat skutečně podaným verzím; automatické rozpoznání doručení správci daně aplikace nemá.
