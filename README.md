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
- Odeslání exportovaných XML příslušnému finančnímu úřadu datovou schránkou včetně stažení ZFO odeslané zprávy a doručenky.
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

## Odeslání datovou schránkou

Tlačítko **Odeslat** v horní liště pošle exportovaná XML vybraného období příslušnému finančnímu úřadu datovou schránkou (webové služby ISDS) a hned k nim stáhne ZFO odeslané zprávy a doručenku.

- Aplikace eviduje každé vyexportované XML jako podání čekající na odeslání. Tlačítko je aktivní u každého období, které má neodeslaná XML – nebo u kterého ještě chybí stažené ZFO či doručenka. V seznamu období je stav vidět jako příznak `k odeslání`, `chybí doručenka`, nebo `odesláno`.
- Přiznání a kontrolní hlášení jsou dvě samostatná podání, proto jdou jako **dvě samostatné datové zprávy**, každá s jedním XML v příloze a s věcí, ze které je podání poznat (např. „Řádné přiznání k DPH za 06/2026, DIČ CZ…“).
- Příjemce se určuje podle finančního úřadu poplatníka podle [seznamu ID kódů orgánů finanční správy ČR](https://financnisprava.gov.cz/cs/dane/dane-elektronicky/datove-schranky/seznam-id-kodu-organu-financni-spravy-cr). Adresátem je vždy datová schránka finančního úřadu (kraje), ne územního pracoviště; územní pracoviště se uvádí jen v XML.
- ZFO se ukládají vedle odeslaného XML jako `<název>_<ID zprávy>_zprava.zfo` (odeslaná zpráva) a `<název>_<ID zprávy>_dorucenka.zfo` (doručenka). ID zprávy v názvu zajistí, že opakovaný export a odeslání téhož souboru nepřepíše důkaz o dřívějším podání. Pokud ISDS doručenku hned nevydá, podání zůstane označené jako nedokončené a dalším stiskem tlačítka **Odeslat** se jen dotáhne – znovu se neodesílá.
- Odeslání se do evidence zapíše dřív, než se stahují ZFO. Ani při výpadku sítě nebo pádu aplikace se tedy podání neodešle podruhé.

### Nejisté odeslání

Pokud se spojení přeruší v okamžiku, kdy už zpráva mohla u úřadu vzniknout (výpadek sítě, vypršení časového limitu, chyba serveru 5xx), aplikace podání **neoznačí za neodeslané**. Každá odesílaná zpráva nese jednoznačnou značku v poli „číslo jednací odesílatele“ (`dmSenderRefNumber`), a období dostane příznak `ověřit odeslání`.

Při dalším stisku tlačítka **Odeslat** se nejdřív prohledá seznam odeslaných zpráv v datové schránce:

- **zpráva se značkou se najde** – podání se označí za odeslané a podruhé se neposílá,
- **nenajde se** – dřívější pokus zprávu nevytvořil a podání se odešle normálně,
- **ověření samo selže** (síť je pořád nedostupná) – podání se pro jistotu neodešle a zůstane k ověření.

Díky tomu ani ztracená odpověď nevede k tomu, že by u finančního úřadu leželo stejné podání dvakrát. Naproti tomu odmítnutí se stavovým kódem ISDS je jednoznačné – zpráva nevznikla a podání jde rovnou odeslat znovu.

Export a odesílání se nemůžou překrývat – pracují se stejnou evidencí podání a souběh by mohl smazat záznam právě odesílané zprávy. Během odesílání je export zablokovaný a během exportu je neaktivní tlačítko **Odeslat**. Kdyby přesto záznam odeslané zprávy zmizel, aplikace ho doplní zpět, aby o podání nikdy nechyběla stopa.

Dokud u souboru visí neověřený pokus, **neodešle se ani jeho novější export** – šlo by o duplicitní podání téhož dokumentu. Ostatních souborů se to netýká: nejistota u přiznání nezdrží kontrolní hlášení. Dokud období čeká na ověření, je navíc **export nových XML zablokovaný**: nedá se rozhodnout mezi řádným a opravným podáním a nový soubor by přepsal ten, o který jde. Značka pro ověření se nesmaže ani při exportu z jiné cesty – teprve když se ukáže, že pokus zprávu nevytvořil, překonaný záznam zmizí a odešle se novější export.
- Před odesláním se zobrazí potvrzení se seznamem souborů a adresátem. **Odeslanou datovou zprávu nelze vzít zpět.**
- Exporty pořízené staršími verzemi aplikace se zpětně neevidují – u nich zůstává tlačítko neaktivní, protože aplikace neví, zda už byly podané jinou cestou. Odesílat lze XML vyexportovaná touto a novějšími verzemi.

### Přihlašovací údaje

Jméno a heslo do datové schránky se zadají při prvním odeslání, ověří se proti ISDS a uloží se do profilu uživatele:

- **Windows** – zašifrované DPAPI klíčem odvozeným od účtu uživatele; soubor nerozšifruje jiný uživatel ani jiný počítač.
- **Linux a macOS** – zašifrované AES-GCM klíčem v samostatném souboru s právy `0600`. Ochrana zde stojí na právech k souborům v domovském adresáři, tedy na stejné úrovni jako lokální databáze aplikace.

Když ISDS uložené heslo odmítne kdykoli během odesílání – včetně pouhého dotahování doručenek – aplikace ho zahodí a při dalším pokusu se zeptá znovu.

Podporuje se přihlášení jménem a heslem. Schránky zabezpečené jednorázovým heslem (SMS/TOTP) nebo přihlášením certifikátem aplikace neumí – ISDS takové přihlášení odmítne a aplikace to oznámí. Uložené údaje jde smazat na kartě **Import** tlačítkem **Zapomenout přihlášení**; při dalším odeslání se aplikace zeptá znovu. Pokud ISDS uložené heslo odmítne (např. po jeho změně), aplikace ho smaže sama.

Odeslání datové zprávy není potvrzení, že podání prošlo kontrolou EPO. Doručenka dokládá doručení správci daně; věcné přijetí podání ověřte v portálu MOJE daně.

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
<LocalApplicationData>/DphAssistant/isds-credentials.dat
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
- `src/Dph.Core` - doména, výpočty DPH, EPO XML import/export, odesílání datovou schránkou (ISDS), ARES/ČNB služby, PDF faktury a persistence.
- `tests/Dph.Core.Tests` - testy výpočtů, importu/exportu, odesílání do datové schránky, repository, ARES/ČNB pomocných služeb a PDF rendereru.

## Licence

Projekt je dostupný pod licencí MIT. Viz [LICENSE](LICENSE).

## Podklady pro daňová pravidla

Zdroje použité při revizi daňových výpočtů:

- [Pokyny Finanční správy k přiznání DPH, vzor 22](https://financnisprava.gov.cz/assets/tiskopisy/5412_22.pdf): rozdělení plnění a odpočtů do řádků, rozdílové dodatečné přiznání.
- [Metodická informace ke KH od 1. 1. 2024](https://financnisprava.gov.cz/assets/cs/prilohy/d-seznam-dani/Metodicka_informace_k_vyplneni_KH_20240101.pdf): limit dokladu, záporné opravy, identifikace protistran, částečný odpočet.
- [Informace GFŘ k Brexitu](https://financnisprava.gov.cz/assets/cs/prilohy/d-seznam-dani/Info-dopady-BREXITu-na-DPH-od-20210101.pdf): Severní Irsko v režimu služeb.
- [Výpočet a zaokrouhlování DPH](https://financnisprava.gov.cz/cs/financni-sprava/novinky/novinky-2019/vypocet-dph-a-zaokrouhlovani-od-1-10-2019): metody výpočtu daně; aktuální sazby určuje § 47 ZDPH.
- [Aktuální struktura DPHDP3](https://adisspr.mfcr.cz/dpr/adis/idpr_pub/epo2_info/popis_struktury_detail.faces?zkratka=DPHDP3): pole přiznání a skutečný den zjištění důvodů dodatečného podání.
- [WSDL webových služeb ISDS](https://www.mojedatovaschranka.cz/static/wsdl/v20/dm_operations.wsdl): rozhraní pro odeslání datové zprávy a stažení podepsané zprávy a doručenky.
- [WSDL informačních služeb ISDS](https://www.mojedatovaschranka.cz/static/wsdl/v20/dm_info.wsdl): doručenka a seznam odeslaných zpráv (dohledání zprávy po nejistém odeslání).
- [Seznam ID kódů orgánů finanční správy ČR](https://financnisprava.gov.cz/cs/dane/dane-elektronicky/datove-schranky/seznam-id-kodu-organu-financni-spravy-cr): ID datových schránek finančních úřadů.

Vygenerování XML samo o sobě není podání. Podání vzniká až odesláním – z aplikace datovou schránkou (viz [Odeslání datovou schránkou](#odeslání-datovou-schránkou)), nebo ručně přes portál MOJE daně. Historie exportních/importních souborů musí odpovídat skutečně podaným verzím; u XML podaných mimo aplikaci nemá aplikace jak doručení rozpoznat.
