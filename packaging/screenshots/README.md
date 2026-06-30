# Screenshoty pro obchody (Flathub / GNOME Software)

AppStream metainfo (`packaging/linux/io.github.koudelkab.DphAsistent.metainfo.xml`)
odkazuje na obrázky z tohoto adresáře přes `raw.githubusercontent.com`. Aby se
screenshoty zobrazily v obchodech a prošla validace Flathubu, musí tu ležet
**skutečné soubory se shodnými názvy** a být pushnuté do větve `main`.

## Očekávané soubory

| Soubor | Popisek |
|--------|---------|
| `01-prehled.png` | Přehled období DPH a evidence dokladů (hlavní – `type="default"`) |
| `02-faktura.png` | Vystavení faktury |
| `03-subjekty.png` | Adresář odběratelů a dodavatelů |

Další screenshoty můžeš přidat – stačí doplnit `<screenshot>` do metainfo a soubor sem.

## Požadavky (Flathub)

- formát **PNG** (bez průhlednosti),
- bez stínu okna a bez kurzoru,
- šířka **max 1600 px** (ideálně nativní velikost okna ~1240×760),
- alespoň jeden screenshot, první je `type="default"`,
- ideálně světlé téma a reálná (anonymizovaná) data.

## Jak pořídit (Fedora/GNOME)

1. Spusť aplikaci.
2. Vyfoť **jen okno** appky (GNOME: `Print Screen` → výběr okna, nebo nástroj „Snímek obrazovky“).
3. Ulož sem pod správným názvem, commitni a pushni do `main`.

> Tip: po nahrání ověř, že URL funguje, např.:
> `https://raw.githubusercontent.com/KoudelkaB/DPH-Asistent/main/packaging/screenshots/01-prehled.png`
