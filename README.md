# Fotoalbum

Aplikace pro Windows na popisování fotografií z cest. Popisky se ukládají přímo do metadat JPG
(XMP `dc:description`, IPTC `Caption-Abstract`, EXIF `ImageDescription`), takže je uvidí i jiné programy.

Zadání: [docs/SPECIFIKACE.md](docs/SPECIFIKACE.md) · maketa: [docs/maketa/fotoalbum-maketa.html](docs/maketa/fotoalbum-maketa.html)

## ⚠️ Veřejný repozitář

Do repozitáře **nikdy** neukládejte API klíče, hesla ani rodinné fotografie. Klíče se zadávají až v aplikaci
na konkrétním počítači. Podrobnosti v kapitole 8 specifikace.

## Stav: etapa 1 (MVP)

Hotovo:
- strom složek s počty vyřízených fotek, druh složky (cestovní / běžné fotky),
- pás miniatur, barevný pruh postupu, „Další nepopsaná“, filtry,
- velká fotka s šipkami, otočení,
- popisek: ukládá se sám (do databáze hned, do fotky po chvíli / při přechodu na jinou fotku / při zavření), Zpět / Znovu, historie verzí v databázi,
- stavy fotky: popsáno, bez popisku (klávesa **B**), označeno ke smazání,
- mazání jen do koše Windows a až po potvrzení při zavření programu,
- pokračování tam, kde se minule skončilo,
- záloha databáze do složky `Fotoalbum – databáze (nemazat)\zaloha` ve složce s fotkami (zálohuje ji Synology Drive),
- úpravy po prvním testu (specifikace 2.7): štítek „Dovolená“, popisek se ukládá až při další akci, Zpět/Znovu po celých akcích,
  tlačítko „Bez popisku – další fotka“, nadpis fotky (Windows „Název“), vlastní ikona.

**Kopírování z karty** (součást aplikace): Fotoalbum běží na pozadí jako ikona u hodin a spouští se s Windows.
Po zasunutí karty z fotoaparátu do čtečky (DCIM/100_PANA…) samo zkopíruje nové fotky a videa do složek „rok období“
(např. „2026 léto“; „2026 zima“ = prosinec 2026 až únor 2027) a ukáže průběh. Karta se nemění. „Zavřít program“ zavře okno, úplné ukončení je v menu ikony.

Další etapy (diktování, AI, knihovna alb, čtení, tablet, televize) – viz specifikace, kap. „Etapy“.

## Ovládání

| Akce | Ovládání |
|---|---|
| Předchozí / další fotka | šipky na fotce, tlačítka, klávesy ← → (mimo psaní), PageUp / PageDown (i při psaní) |
| Bez popisku (a hned další fotka) | tlačítko „Bez popisku – další fotka“, klávesa **B** |
| Zpět / Znovu (celé akce) | tlačítka, Ctrl+Z / Ctrl+Y (mimo psaní) |
| Nastavení (složka s fotkami) | **Ctrl+Alt+S** – pro správce |

## Struktura

```
src/Fotoalbum.Core      jádro: knihovna fotek, databáze (SQLite), ExifTool, náhledy (SkiaSharp)
src/Fotoalbum.Server    lokální server + webové rozhraní (ui/), stejné rozhraní později pro tablet
src/Fotoalbum.Desktop   aplikace pro Windows: ikona u hodin, okno s WebView2, kopírování z karty, koš Windows
tests/                  testy jádra (xUnit)
```

Aplikace pro Windows spouští server jen pro tento počítač (`127.0.0.1`, náhodný port, jednorázový přístupový klíč).
Data aplikace jsou v `%LOCALAPPDATA%\Fotoalbum` (databáze `katalog.db`, náhledy, nastavení).

## Vývoj

Potřeba: .NET SDK 10, ExifTool.

```bash
dotnet build
dotnet test                                   # testy potřebují exiftool v PATH (Linux: apt install libimage-exiftool-perl)

# rozhraní v prohlížeči (Linux i Windows), adresu vypíše konzole:
dotnet run --project src/Fotoalbum.Server -- --data ./tmp-data --url http://127.0.0.1:5180
```

Aplikace pro Windows:

```powershell
dotnet publish src/Fotoalbum.Desktop -c Release -r win-x64 --self-contained -o out/Fotoalbum
```

ExifTool pro Windows (https://exiftool.org, „Windows Executable“) rozbalte do `out/Fotoalbum/exiftool/`
a soubor `exiftool(-k).exe` přejmenujte na `exiftool.exe`. GitHub Actions to dělají automaticky –
hotový balíček je v záložce **Actions** jako artefakt `Fotoalbum-win-x64`.

## Licence použitých součástí

.NET, Microsoft.Data.Sqlite, WebView2 (Microsoft), SkiaSharp (MIT), ExifTool (Phil Harvey, licence Perl / GPL – přibaluje se jako samostatný program).
