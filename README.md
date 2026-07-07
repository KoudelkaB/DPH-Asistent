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
- Evidence vydaných a přijatých dokladů; u přijatých se tuzemský režim vs. reverse charge
  (přijetí služby ze zahraničí) rozpozná automaticky z DIČ dodavatele.
- Automatický výpočet základu, DPH a částky s DPH podle sazby.
- Podpora sazeb DPH 21 %, 12 % a 0 %.
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

O zařazení přijatého dokladu (tuzemský odpočet vs. reverse charge, EU vs. třetí země) rozhoduje aplikace sama podle prefixu DIČ dodavatele – uživatel v UI volí jen Vydaná/Přijatá. Nejde o tuzemský režim přenesení daňové povinnosti podle §92a (ř. 10/11, KH B.1), ten aplikace nemodeluje.

Při opakovaném exportu již podaného období se aplikace řídí lhůtou pro podání (25. den následujícího měsíce, posunutý na nejbližší pracovní den):

- **Do lhůty** nabídne řádné (přepíše stávající XML), nebo **opravné** přiznání a kontrolní hlášení (forma „O“).
- **Po lhůtě** vygeneruje **dodatečné přiznání** (forma „D“, jen rozdíly oproti poslední známé dani na ř. 66, s datem zjištění) a **následné kontrolní hlášení** (forma „N“, kompletní data). Rozdíly se počítají proti hodnotám skutečně vykázaným v naposledy podaném XML; beze změny plnění se dodatečné přiznání nepodává a vznikne jen následné kontrolní hlášení.

Opravné, dodatečné i následné exporty dostávají samostatný název souboru, aby nepřepsaly předchozí podání.

## Vydané faktury

Samostatná agenda vydaných faktur umožňuje:

- založit novou fakturu s automatickým číslem,
- použít existující fakturu jako šablonu,
- evidovat odběratele ručně, z adresáře nebo doplněním z ARES,
- zadat položky faktury s množstvím, měrnou jednotkou, cenou za jednotku a sazbou DPH,
- automaticky spočítat základ, DPH a celkovou částku,
- nastavit datum vystavení, DUZP, splatnost, variabilní symbol a měnu,
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
- importuje dokladové řádky, pokud období ještě nemá vlastní řádky,
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
dotnet test tests/Dph.Core.Tests/Dph.Core.Tests.csproj
```

Verze aplikace se neudržuje ručně – generuje ji [MinVer](https://github.com/adamralph/minver) z git tagů `vX.Y.Z`. Postup vydání je v [PUBLISHING.md](PUBLISHING.md).

## Struktura projektu

- `src/Dph.App` - Avalonia desktopová aplikace a viewmodely.
- `src/Dph.Core` - doména, výpočty DPH, EPO XML import/export, ARES/ČNB služby, PDF faktury a persistence.
- `tests/Dph.Core.Tests` - testy výpočtů, importu/exportu, repository, ARES/ČNB pomocných služeb a PDF rendereru.

## Licence

Projekt je dostupný pod licencí MIT. Viz [LICENSE](LICENSE).
