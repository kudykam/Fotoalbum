# Fotokronika – stav práce a rozdělané úkoly

*Zapsáno 19. 11. 2026 při archivaci vývojového vlákna. Závazné zadání je v [SPECIFIKACE.md](SPECIFIKACE.md),
pravidla pro vývoj v [AGENTS.md](../AGENTS.md). Tento soubor říká, kde se skončilo a co dál.*

## 1. Kde je co

| Co | Kde |
|---|---|
| Hlavní větev (výchozí v repozitáři) | `claude/fotoalbum-windows-app-04txi6` – stav po verzi **0.4.0-test.5** + zápis poznatků z testu (spec. 2.15) |
| Nejnovější vydaná verze | **v0.4.0-test.6** (15. 11. 2026), vydaná z větve **`codex/fotokronika-test-6`** (commit `0fa2694`) |
| Rozpracováno mimo hlavní větev | větev `codex/fotokronika-test-6`: 4 commity nad hlavní větví (z toho 2 až po vydání test.6) – **není sloučeno do hlavní větve** |
| Staré větve | `codex/fotokronika-upravy-specifikace` (PR #2, už obsažena v hlavní větvi – PR lze zavřít), `add-claude-md` (PR #1) |
| Vydání | GitHub Releases, testovací kanál (pre-release). **Stabilní verze pro dědu zatím žádná.** |

Commity na `codex/fotokronika-test-6` (od nejstaršího):
1. `13c84b6` Zrychli načítání a oprav zobrazení fotek, filtry a dokončení složek
2. `0fa2694` Filtruj dokončení složek pouze ve stromu ← **v0.4.0-test.6**
3. `7b62e5c` Oprav nabídku složek, dlaždice a listování ve Čtení
4. `303bbe8` Při načtení přehledu nahraď dočasné dlaždice ověřenými alby

## 2. První kroky v novém vlákně

1. **Sloučit `codex/fotokronika-test-6` do hlavní větve** (jde rychle dopředu, větev vychází z `5b0909b`; jen tento soubor STAV-PRACE.md přibyl na hlavní větvi navíc). Před sloučením projít změny a spustit `dotnet test` (měly by projít; ověřit).
2. Ověřit v prohlížeči (fake AI, viz kap. 5), že body ze spec. **2.15** fungují, a odškrtat je v tabulce níže.
3. Další vydání číslovat **0.4.0-test.7** (test.6 už existuje). **Nová verze jen na pokyn uživatele.**

## 3. Rozdělané – poznatky z testu 0.4.0-test.5 (spec. 2.15)

Codex je zapsal do specifikace a na větvi `codex/fotokronika-test-6` na nich pracoval. **Nikdo je zatím neověřil.**

| Bod (spec. 2.15) | Stav |
|---|---|
| Palma / tučně u nově označené dovolené se ukáže hned (u NASu dnes 10–15 s) | rozpracováno na větvi codex, ověřit |
| „Vrátit označení složky jako hotové“ v menu pravého tlačítka i po zmizení potvrzení; vrací jen Bez popisku z hromadného dokončení | ověřit |
| Ctrl+klik ve stromu = výběr více složek, hromadně označit / zrušit dovolenou | ověřit |
| Velká složka v Pracovně: ukázat fotky hned z katalogu, metadata doplnit na pozadí, strom při obnově neposkakuje | ověřit |
| Přehled Čtení: dlaždice hned (aspoň název), náhledy doplnit průběžně; nová dovolená vidět hned po přepnutí do Čtení | ověřit (commit `303bbe8`) |
| Dlaždice: chybějící složky skrýt, pravým tlačítkem zrušit dovolenou, náhradní obal | ověřit (commit `7b62e5c`) |
| První fotka ve Čtení bez prodlevy (ostatní fotky alba načítat postupně) | ověřit |
| Fotka ve Čtení vždy celá (i na výšku/otočená), popisek v poloprůhledném rámečku přes fotku, bez bílé stránky „knihy“ (kniha jen pro úvod alba) | ověřit |
| Celá obrazovka: jen fotka + popisek, listování šipkami / klepnutím na půlku, Esc zpět | ověřit ve Windows |
| Filtr složek **Vše / Popsané / Nepopsané** v Pracovně i ve Čtení – mění **jen strom**, ne fotky ani dlaždice; předci zůstávají kvůli navigaci | ověřit |

## 4. Plán dalších etap (zatím neděláno)

Podle spec. kap. 10 „Etapy“ a 2.x:

1. **Staré vepsané popisky ve fotkách** (kap. 7): přečíst text z obrazu → podklad popisku, odstranit domalováním, originál do „Fotokronika – originály (nemazat)“, náhled před/po, šablona oblasti. **Čeká na 2–3 ukázkové fotky od uživatele (soukromě, ne do repozitáře).**
2. **Promítání na TV** Samsung UE46F5400 (2.3.1) a **vlastní DLNA server** (2.3.2) – **dělat až na výslovný pokyn**.
3. **Tablet (Android) jako druhá obrazovka PC** (kap. 9): poslech v domácí síti, párování QR kódem, probuzení PC (Wake-on-LAN).
4. Drobnosti: „Přesunout vybrané fotky do nové dovolené“ (2.7), přehrávání videí z karty, přibalit písma do aplikace, čitelný strom při hlubokém vnoření (2.10 – zkracování „…“, vodicí čáry, drobečková navigace; částečně hotovo).
5. **Stabilní verze pro dědu** – až uživatel řekne „vydej to dědovi“ (tag `vX.Y.Z` bez `-test`).

## 5. Ještě neověřeno ve skutečnosti

- Diktování a korektura **se skutečným klíčem OpenRouteru** (přesnost přepisu, styl popisků, cena). Vývoj byl jen s náhradní AI.
- Hlasové vyhledávání se skutečnou AI.
- Celá obrazovka ve Windows (WebView2 → okno bez rámečku přes hlavní panel, `MainForm.SetFullScreen`).
- Procházení celé knihovny na pozadí u tisíců fotek (rychlost, zátěž NASu).

## 6. Otevřené otázky na uživatele

1. Ukázkové fotky s vepsaným textem (etapa „staré popisky“).
2. Synology Drive u dědy: synchronizace, nebo zálohování? Zapnuté verzování na NAS?
3. Model a velikost dědova Android tabletu.

## 7. Jak vyvíjet a vydávat (praktické poznámky)

- Sestavení a testy: `dotnet build`, `dotnet test` (testy potřebují `exiftool` v PATH).
- Rozhraní v prohlížeči s náhradní AI (nic se neposílá na internet):
  `FOTOALBUM_FAKE_AI=1 dotnet run --project src/Fotoalbum.Server -- --data ./tmp-data --url http://127.0.0.1:5180`
  (adresu s přístupovým klíčem vypíše konzole). Server má rozhraní zabudované – **po změně v `ui/` je nutné server znovu sestavit a restartovat**.
- Testy rozhraní jsme dělali Playwrightem (Chromium s `--use-fake-device-for-media-stream --use-file-for-fake-audio-capture=nahravka.wav` pro diktování). Nahrávku hlasu uživatele **nikdy necommitovat**.
- **Vydání:** GitHub Actions workflow `release.yml` → *Run workflow* s parametrem `version` (např. `0.4.0-test.7`). Push tagů ze sezení Claude je blokovaný, proto přes ruční spuštění workflow. Testovací verze = `-test.N` (pre-release), stabilní = bez přípony.
- `vpk pack` běží s `--delta none` – starší vydání měla v názvu „ř“ (Fotokronikář) a vpk s tím rozdílové balíčky neumí. Aktualizace se proto stahují celé (~85 MB).
- Aplikace se jmenuje **Fotokronika**; vnitřní názvy zůstávají `Fotoalbum` (exe, `%LOCALAPPDATA%\Fotoalbum`, balíček `Fotoalbum.App`) kvůli aktualizacím a datům.
- **Repozitář je veřejný:** žádné klíče, hesla, `.env`, rodinné fotky ani nahrávky.
