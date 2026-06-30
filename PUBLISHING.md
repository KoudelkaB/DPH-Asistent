# Publikování DPH Asistent

Tento dokument popisuje, jak se sestavují instalační balíčky a jak aplikaci publikovat
na **Flathub** a do **Winget**.

App ID / identifikátory:

| Platforma | Identifikátor |
|-----------|---------------|
| Flatpak / Flathub | `io.github.koudelkab.DphAsistent` |
| Winget | `BohdanKoudelka.DPHAsistent` |
| Windows uninstall (Inno Setup) | `{21824057-8CF5-4A0C-8CFD-55B8688ECEDA}_is1` |

> **Ikona:** master je [`packaging/icons/io.github.koudelkab.DphAsistent.svg`](packaging/icons/io.github.koudelkab.DphAsistent.svg)
> (originální grafika – doklad „DPH“ se zeleným zaškrtnutím). PNG sady a Windows `.ico`
> (`src/Dph.App/Assets/dph-asistent.ico`) se z něj generují přes Inkscape/ImageMagick:
>
> ```bash
> SVG=packaging/icons/io.github.koudelkab.DphAsistent.svg
> for s in 16 32 48 64 128 256; do
>   inkscape "$SVG" --export-type=png -w $s -h $s \
>     --export-filename="packaging/icons/${s}x${s}/io.github.koudelkab.DphAsistent.png"
> done
> ```

---

## 1. Vydání nové verze (GitHub Release)

Verze je řízena tagem. Workflow [`.github/workflows/release.yml`](.github/workflows/release.yml)
se spustí po pushnutí tagu `vX.Y.Z` a sestaví:

- Windows Inno Setup instalátor (`DphAsistent-Setup-X.Y.Z.exe`),
- Windows přenosný zip,
- Linux Flatpak bundle (`DphAsistent-X.Y.Z.flatpak`),
- Linux přenosný `tar.gz`,
- `SHA256SUMS.txt`,

a vytvoří **draft** GitHub Release s těmito artefakty.

```bash
# 1) Sjednoťte verzi (Directory.Build.props -> <Version>) a commitněte.
# 2) Vytvořte a pushněte tag:
git tag v0.1.0
git push origin v0.1.0
# 3) Počkejte na workflow, zkontrolujte draft Release na GitHubu a publikujte ho.
```

Ruční zkušební build bez tagu: GitHub → Actions → *Release* → *Run workflow*
(artefakty se nahrají, Release se nevytvoří).

Verzi lze do buildu předat i lokálně přes `-p:Version=0.1.0`.

---

## 2. Lokální sestavení instalátorů (volitelné)

### Windows (Inno Setup)

Vyžaduje [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet publish src\Dph.App\Dph.App.csproj -c Release -r win-x64 --self-contained `
  -p:Version=0.1.0 -o publish\win-x64
iscc /DAppVersion=0.1.0 /DSourceDir="publish\win-x64" packaging\windows\DphAsistent.iss
# výsledek: dist\DphAsistent-Setup-0.1.0.exe
```

### Linux (Flatpak)

Vyžaduje `flatpak` + `flatpak-builder` a runtime `org.freedesktop.{Platform,Sdk}//24.08`.

```bash
dotnet publish src/Dph.App/Dph.App.csproj -c Release -r linux-x64 --self-contained \
  -p:Version=0.1.0 -o publish/linux-x64

# Naplnění staging adresáře, ze kterého manifest balí:
STAGE=packaging/flatpak/staging
rm -rf "$STAGE" && mkdir -p "$STAGE/publish"
cp -r publish/linux-x64/. "$STAGE/publish/"
cp packaging/linux/io.github.koudelkab.DphAsistent.desktop "$STAGE/"
cp packaging/linux/io.github.koudelkab.DphAsistent.metainfo.xml "$STAGE/"
cp -r packaging/icons "$STAGE/icons"

flatpak-builder --user --force-clean --repo=_flatpak_repo _flatpak_build \
  packaging/flatpak/io.github.koudelkab.DphAsistent.yaml
flatpak build-bundle _flatpak_repo dist/DphAsistent-0.1.0.flatpak \
  io.github.koudelkab.DphAsistent
# instalace: flatpak install --user dist/DphAsistent-0.1.0.flatpak
```

---

## 3. Flathub

Manifest [`packaging/flatpak/io.github.koudelkab.DphAsistent.yaml`](packaging/flatpak/io.github.koudelkab.DphAsistent.yaml)
balí **předem sestavený** self-contained výstup (adresář `staging/`). Tím vzniká
přímo instalovatelný `.flatpak` v CI a v GitHub Release.

Oficiální Flathub buildbot ale staví **ze zdrojů bez přístupu k síti**, takže pro
submission na Flathub je potřeba manifest, který:

1. staví přes `org.freedesktop.Sdk.Extension.dotnetX` (verze dle dostupnosti pro .NET 10),
2. má NuGet balíčky vendorované offline přes generátor
   [`flatpak-builder-tools/dotnet`](https://github.com/flatpak/flatpak-builder-tools/tree/master/dotnet):

```bash
# vygeneruje nuget-sources.json z lock souboru
python3 flatpak-dotnet-generator.py nuget-sources.json src/Dph.App/Dph.App.csproj \
  --dotnet 10 --runtime linux-x64
```

Postup submission:

1. Fork [`flathub/flathub`](https://github.com/flathub/flathub) (větev `new-pr`).
2. Přidej `io.github.koudelkab.DphAsistent.yaml` (source-build varianta) +
   `nuget-sources.json` + metainfo + desktop + ikony.
3. Ověř lokálně: `flatpak run org.flatpak.Builder --user --install ...` a
   `flatpak run --command=appstreamcli org.flatpak.Builder validate ...metainfo.xml`.
4. Otevři PR; po schválení vznikne repozitář `flathub/io.github.koudelkab.DphAsistent`.

> Tip: AppStream metainfo a `.desktop` v `packaging/linux/` jsou už ve formě, kterou
> Flathub vyžaduje (RDNS id, `metadata_license`, `project_license`, screenshoty doplň
> později do `<screenshots>`).

---

## 4. Winget

Připravené manifesty (schema 1.6.0) jsou v
[`packaging/winget/manifests/b/BohdanKoudelka/DPHAsistent/0.1.0/`](packaging/winget/manifests/b/BohdanKoudelka/DPHAsistent/0.1.0/).

Po vydání GitHub Release:

1. **Doplň `InstallerSha256`** v `*.installer.yaml` skutečným hashem instalátoru:
   ```bash
   sha256sum DphAsistent-Setup-0.1.0.exe   # nebo: gh release download v0.1.0 -p '*.exe'
   ```
   (Hash je i v `SHA256SUMS.txt` v Release.)
2. Ověř manifesty:
   ```bash
   winget validate --manifest packaging/winget/manifests/b/BohdanKoudelka/DPHAsistent/0.1.0
   winget install --manifest packaging/winget/manifests/b/BohdanKoudelka/DPHAsistent/0.1.0
   ```
3. Otevři PR do [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs)
   (zkopíruj adresář `manifests/b/...` do forku), nebo použij
   [`wingetcreate`](https://github.com/microsoft/winget-create):
   ```bash
   wingetcreate update BohdanKoudelka.DPHAsistent \
     --version 0.1.0 \
     --urls https://github.com/KoudelkaB/DPH-Asistent/releases/download/v0.1.0/DphAsistent-Setup-0.1.0.exe \
     --submit
   ```
   `wingetcreate` SHA256 dopočítá sám.

Pro další verze stačí zkopírovat adresář `0.1.0/` na novou verzi, upravit `PackageVersion`,
`InstallerUrl`, `ReleaseDate` a `InstallerSha256`.
