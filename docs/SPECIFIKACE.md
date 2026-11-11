# Fotoalbum – specifikace (verze 0.1, návrh)

> Stav: **plánování**. Dokument shrnuje dosud odsouhlasené zadání. Změna požadavku se nejdřív zapíše sem, teprve potom do kódu.

## 1. Účel

Aplikace pro Windows, ve které si děda prochází tisíce vlastních digitálních fotografií (JPG), diktuje k nim popisky a s pomocí AI z nich vytváří hezká alba s čitelnými, poutavými popisky ve stylu **neosobního cestopisu**. Výsledný popisek se ukládá přímo do metadat fotografie.

Hlavní uživatel je senior → **velká písmena, velká tlačítka s ikonou i textem, minimum dialogů, nic nevratného**.

## 2. Obrazovky

### 2.1 Knihovna (hlavní stránka)
- Dlaždice alb: titulní fotka, název, období, postup („86 % popsáno“).
- Album = složka ve stromu fotek.
- Řazení podle **data** nebo **názvu**; vyhledávání v popiscích.
- Album může mít **úvodní text** (diktuje se a vylepšuje stejně jako popisky).
- Nahoře velké tlačítko **„Pokračovat: Afrika 1998 › Keňa, fotka 214 z 500“** – jedním stiskem otevře Pracovnu přesně tam, kde se minule skončilo (i když to bylo na jiném zařízení). Viz 2.5.

### 2.2 Pracovna (popisování)
Cílový monitor: 21″, 1920×1080. **Schváleno: popisek a ovládání jsou VEDLE fotky (vpravo)** – fotky 3:2 / 4:3 tak využijí výšku obrazovky a nevzniká prázdná plocha po stranách.

```
┌──────────────┬───────────────────────────────────────────────────────────┐
│ Strom složek │ pás miniatur ◀ ▢ ▢ ▣ ▢ ▢ ▢ ▢ ▶            Fotka 214 z 500 │
│ (lze sbalit) │ ┌────────────────────────────────────┐ ┌────────────────┐ │
│              │ │                                    │ │ starý popisek? │ │
│              │ │                                    │ ├─ Popisek ──────┤ │
│              │ │◀          VELKÁ FOTKA             ▶│ │ (vysoké pole,  │ │
│              │ │                                    │ │  natáhne se)   │ │
│              │ │                                    │ ├────────────────┤ │
│              │ │                                    │ │ [🎤 DIKTOVAT ] │ │
│              │ │                                    │ │ [✨ Vylepšit ] │ │
│ Filtry       │ │                                    │ │ [◀ Předch][Další ▶]│
│              │ └────────────────────────────────────┘ │ [↶ Zpět][↷ Znovu]│ │
│              │                                        │ [Smazat][⋯ Více] │ │
└──────────────┴────────────────────────────────────────┴──────────────────┘
```

- Pravý panel: upozornění na starý vepsaný popisek → pole popisku (vyplní volnou výšku) → **Diktovat** (největší tlačítko, přes celou šířku) → **Vylepšit text** → Předchozí / Další → Zpět / Znovu → Smazat popisek / Více.
- Na okrajích fotky velké poloprůhledné šipky ◀ ▶ pro předchozí / další fotku.
- Okno aplikace využívá celou šířku monitoru.
- Varianta „popisek pod fotkou“ zůstane jen jako volba v nastavení (pro jiný poměr stran monitoru), výchozí je vedle.
- Miniatury mají značku stavu (viz 2.5).
- Filtry: „jen nepopsané“, „skrýt běžné fotky“.
- Otočení fotky ↻ (v menu ⋯).
- Maketa (Knihovna, Pracovna, Čtení, náhled tabletu): https://claude.ai/artifact/WzmcgPRpu6q2vw9fHcCVfy (soukromý odkaz) a kopie v repozitáři `docs/maketa/fotoalbum-maketa.html` (otevřít v prohlížeči; ukázková data).

### 2.3 Čtecí režim
- Celá obrazovka, vzhled listování albem (fotka + popisek), ovládání šipkami.
- Konkrétní vzhled se ještě doladí. PDF / fotokniha zatím **mimo rozsah**.

### 2.3.1 Promítání na televizi (plánováno)
TV: **Samsung UE46F5400** (rok 2013, platforma před Tizenem). **Ověřeno: TV má „Screen Mirroring“ i AllShare (DLNA).** **Bez instalace aplikace do TV a bez převodu do MP4** – obraz se posílá živě z tabletu nebo PC po domácí Wi-Fi.

- **Hlavní cesta – „Pustit na televizi“ přes DLNA:** TV funguje jako DLNA přehrávač (Samsung AllShare). Snímky TV posílá **PC aplikace**; ovládat (listovat) jde z PC i z tabletu. PC TV najde v síti a posílá jí jednotlivé „snímky“: fotka + popisek pěkným písmem, složené v rozlišení 1920×1080 (originály se nemění, snímek je dočasný). Děda listuje na tabletu, TV ukazuje. Volitelně automatické přehrávání.
- **Druhá rovnocenná cesta – zrcadlení obrazovky (Screen Mirroring / Miracast)** z tabletu s Androidem (tablet bude Android) (u Samsung Galaxy Tab funkce Smart View). Na TV se zobrazí čtecí režim přesně jako na tabletu. iPad tuto TV zrcadlit neumí (AirPlay TV z roku 2013 nepodporuje).
- Obě cesty vyžadují, aby tablet / PC a TV byly ve **stejné domácí Wi-Fi**. Nic se neposílá do cloudu.
- **Ověřit na skutečné TV:** v menu Zdroj → „Screen Mirroring“ a v nastavení sítě „AllShare / Nastavení AllShare“ – podpora DLNA přehrávání. Jakmile bude k dispozici, otestovat prototypem.

### 2.4 Nastavení (jen pro správce)
- Kořenová složka s fotkami, AI služby a klíče, styl popisků, slovníček jmen a míst.
- Pro dědu skryté (heslo / klávesová zkratka).

### 2.5 Velké složky (stovky fotek) a pokračování
Jedna dovolená může mít **500 a více fotek** a **ne každá potřebuje popisek**.

**Stavy fotky** (barva a značka na miniatuře):

| Stav | Význam | Značka |
|---|---|---|
| Nepopsaná | zatím nikdo neřešil | bez značky |
| Rozepsaná | je diktát, ale ne hotový popisek | ✎ oranžová |
| Popsaná | hotový popisek | ✓ zelená |
| **Bez popisku** | záměrně přeskočena, popisek nepotřebuje | – šedá |
| Ke smazání | označena, smaže se při zavření | ✕ červená |

- Tlačítko / akce **„Bez popisku“** (v menu ⋯ i klávesou) – fotka se počítá jako vyřízená a přeskočí se.
- **Postup složky** = (popsané + bez popisku) / celkem, např. „Vyřízeno 214 z 500“.
- **„Další nepopsaná“** – skok na nejbližší fotku, která ještě nic nemá (přeskočí popsané i „bez popisku“).
- **Pruh postupu** pod pásem miniatur: tenký barevný pruh celé složky (zelená/oranžová/šedá/prázdná), kliknutím se skočí na dané místo – v 500 fotkách je hned vidět, kde je hotovo a kde ne.
- **Přehled složky** (mřížka velkých miniatur se stavy) pro rychlé procházení, výběr i hromadné „bez popisku“.
- **Pokračování:** aplikace si pro každou složku pamatuje poslední fotku a globálně poslední složku. Po otevření složky se pokračuje na posledním místě (první fotka jen u nové složky). Poslední místo je součástí sdílených dat, takže jde pokračovat i na jiném zařízení.
- Čtecí režim ve výchozím stavu ukazuje jen fotky s popiskem.

## 3. Diktování a AI

### 3.1 Dvě vrstvy textu u každé fotky
1. **Surový diktát** – přesný přepis řeči včetně přeřeknutí; nikdy se nemaže.
2. **Výsledný popisek** – vytvořený AI, zapisuje se do fotky.

### 3.2 Pracovní postup
1. Stisk 🎤 = start, další stisk = stop. Během nahrávání výrazný stav „● Nahrávám 0:42“. Pojistka: automatické zastavení po delším tichu.
2. Přepis se zobrazuje **živě** (streamovací přepis řeči, čeština).
3. ✨ **Vylepšit** → AI pochopí, jak to bylo myšleno, a vytvoří srozumitelný, poutavý popisek.
4. **Hlasové opravy**: další diktát typu „není to Keňa, ale Tanzanie, a přidej…“ AI rozpozná jako pokyn k úpravě a změní jen dotčené části.
5. Historie verzí, ↶ Zpět / ↷ Znovu.

### 3.3 Pravidla pro AI
- Styl: neosobní cestopis; délka nastavitelná.
- **Nevymýšlet fakta**, která nebyla nadiktována.
- Kontext pro AI: název složky, datum (EXIF), popisky okolních fotek, slovníček jmen a míst.
- AI běží **pouze v cloudu**. Bez internetu: jasné upozornění, ruční psaní funguje dál.

### 3.4 Služby (předběžně)
- Přepis řeči: cloudová streamovací služba s dobrou češtinou (vybere se po testu).
- Úprava textu a čtení textu z obrázku: Claude (Anthropic API).
- Odstranění vepsaného textu z obrázku: cloudová služba pro úpravu obrázků (inpainting) – vybere se po testu na ukázkových fotkách.

## 4. Ukládání

- Automaticky při přechodu na jinou fotku **a** průběžně během psaní; indikátor „✓ Uloženo“.
- Popisek do JPG metadat: **XMP `dc:description`, IPTC `Caption-Abstract`, EXIF `ImageDescription`** (UTF-8).
- Zápis metadat bez přepočítání obrazových dat (např. ExifTool).
- Surový diktát, historie verzí, stavy fotek, poslední pozice a katalog alb: databáze aplikace – zpočátku lokální (SQLite), s výhledem na sdílenou databázi (kap. 9).
- Datum fotky se bere vždy z EXIF.

## 5. Mazání fotek

- „Smazat fotku“ fotku jen **označí** (zčervená, štítek „Bude smazána“), zůstává viditelná a jde jedním klikem vrátit.
- Při zavření programu souhrn: „Označeno N fotek. Přesunout do koše?“ → přesun do **koše Windows**, nikdy trvale.

## 6. Běžné vs. cestovní fotky

- Běžné fotky zůstávají a lze je také popisovat.
- Označení: **podle složky** (složka = běžná / cestovní) + **ruční výjimka** u jednotlivé fotky.
- Filtr „skrýt běžné fotky“.

## 7. Staré vepsané popisky ve fotkách

Část fotek (celé roky) má v obraze popisek přidaný dříve v editoru. **Vypálené datum se nemění**, řeší se jen popisek.

**Zjištěno:** text je **dole přímo v obraze** (ne na pruhu), většinou na stejném místě a stejným písmem. **Originály bez textu neexistují** → záloha je povinná a nesmí se nikdy mazat automaticky.

1. AI starý text **přečte** a vloží ho jako výchozí podklad popisku (surový diktát).
2. Text se z obrazu **odstraní domalováním** (inpainting) – jen v oblasti textu, zbytek obrazu se nemění.
3. **Šablona oblasti:** protože je text většinou na stejném místě, správce jednou označí oblast pro složku / období a ta se použije hromadně; AI navíc ověří, že je text skutečně uvnitř. Fotky, kde text mimo šablonu přesahuje, se zařadí ke kontrole.
4. Před úpravou se originál přesune do zálohy (`_originaly` vedle fotek nebo nastavená složka); upravená fotka zachová název souboru i všechna metadata.
5. Uživatel vidí náhled **před / po**; hromadně: mřížka výsledků, nevyhovující se jedním klikem vrátí z originálu.
6. Možnost kdykoli obnovit originál z aplikace.

## 8. Bezpečnost a soukromí

**Repozitář je veřejný – nesmí do něj přijít žádný klíč, heslo ani osobní fotografie.**

- API klíče se ukládají jen na počítači uživatele: **Správce pověření Windows** nebo šifrovaně přes **DPAPI** v `%APPDATA%\Fotoalbum`.
- Konfigurace v repozitáři obsahuje jen ukázku bez hodnot (`*.example.json`).
- `.gitignore` blokuje `.env`, `*.local.json`, `secrets*`, klíče a certifikáty.
- Doporučeno zapnout v GitHubu **Secret scanning + Push protection** (Settings → Code security).
- Testovací fotky v repozitáři jen vlastní / bez osob, nikdy rodinné fotky.
- Fotky se do cloudu posílají jen při akcích, které to vyžadují (čtení / odstranění textu); popisky a diktát se posílají jako text.

## 9. PC jako centrum, tablet jako druhá obrazovka

**Výchozí situace u dědy:** PC, tablet a TV jsou ve **stejné domácí Wi-Fi**. Žádný server u dědy není (Orange Pi i NAS jsou u správce v jiném domě). Fotky jsou **na PC** a **Synology Drive Client** je průběžně zálohuje na NAS správce. Děda nic nezapíná ani nenastavuje – **žádný Tailscale ani VPN**.

### Architektura (schváleno jako směr)
```
                    domácí Wi-Fi u dědy
  Tablet  ───────────────►  PC: aplikace Fotoalbum  ───────────►  TV UE46F5400
  (prohlížeč / aplikace)    • originály fotek + databáze          (DLNA snímky)
                            • vestavěný server v síti
                            • AI klíče, zápis metadat
                                     │ Synology Drive Client (už existuje)
                                     ▼
                         NAS u správce: záloha fotek + záloha databáze
```
- **PC je jediný zdroj pravdy.** Žádná obousměrná synchronizace databází → žádné konflikty.
- **Tablet se připojuje k PC** v domácí síti a pracuje přímo s jeho daty (procházení, popisky, čtecí režim, promítání na TV). Podmínka: **PC musí běžet** (aplikace může běžet minimalizovaná / v oznamovací oblasti a startovat s Windows).
- Párování tabletu s PC jednou při instalaci (správce naskenuje QR kód z PC). Server poslouchá jen v domácí síti a přijímá jen spárovaná zařízení.
- **Probuzení PC z tabletu (Wake-on-LAN):** PC je připojené kabelem → když PC spí, tablet zobrazí „Počítač spí – Probudit“ a jedním stiskem ho probudí. Správce jednou zapne WoL v BIOSu a v ovladači síťové karty.
- Tablet bez PC: v první verzi nepodporováno. Případně později „vzít složku s sebou“ (offline kopie + odeslání změn po návratu).

### Záloha na NAS
- Popisky jsou v metadatech JPG → Synology Drive je zálohuje automaticky spolu s fotkami.
- Databáze aplikace (diktáty, historie, stavy, pozice) se **neukládá živá do synchronizované složky** (riziko zálohy rozepsaného souboru). Aplikace pravidelně a při zavření vytvoří **konzistentní zálohu** do `_fotoalbum/zaloha/` uvnitř zálohované složky (posledních N kopií).
- Složka `_originaly` (originály před odstraněním vepsaného textu) se zálohuje také.
- **Ověřit nastavení Synology Drive** (zatím neznámé): v Synology Drive Client na PC je u úlohy uveden typ **„Synchronizace“** (obousměrná) nebo **„Zálohování“** (jednosměrná).
  - Při synchronizaci se smazání fotky na PC promítne i na NAS → na NAS zapnout **verzování** a **koš** (Synology Drive Admin Console → Týmová složka / Moje Drive → Verzování), případně přidat jednosměrnou zálohovací úlohu.
  - Fotky „Ke smazání“ jdou z aplikace jen do koše Windows; obnova z NAS je druhá pojistka.

### AI a klíče
- Klíče jen na PC (šifrovaně, DPAPI). Tablet posílá požadavky přes PC → klíče nikdy na tabletu.

## 10. Technologie (návrh)

- **PC aplikace:** .NET 8 (Windows) s vestavěným webovým serverem (Kestrel) pro tablet; SQLite; ExifTool; DLNA (UPnP AV) odesílání na TV; spuštění s Windows, ikona v oznamovací oblasti.
- **Uživatelské rozhraní:** jedno webové rozhraní (HTML – navazuje na maketu), na PC zobrazené v aplikaci (WebView2), na tabletu v prohlížeči jako „aplikace na ploše“ (PWA). Jeden vzhled, jedna údržba, funguje na Androidu i iPadu.
- **Mikrofon na tabletu:** prohlížeč povolí mikrofon jen přes HTTPS → PC aplikace vytvoří vlastní certifikát, správce ho jednou nainstaluje do tabletu. Záložně diktování přes mikrofon klávesnice tabletu (Gboard / Samsung klávesnice umí česky).
- Pokud by se ukázala potřeba (offline tablet, plynulost), lze později doplnit nativní aplikaci pro Android nad stejným API.
- Cloudové AI služby přes HTTPS (volá je PC).

## 11. Otevřené otázky

1. Ukázkové fotky (2–3) s vepsaným textem pro test čtení a odstranění – dodá uživatel soukromě, ne do repozitáře.
2. Výběr konkrétní služby pro přepis řeči a pro inpainting (po testu na ukázkách).
3. Vzhled čtecího režimu.
4. Synology Drive u dědy: typ úlohy (synchronizace / zálohování) a zapnuté verzování na NAS.
5. Model Android tabletu (velikost displeje, verze Androidu) – kvůli rozložení rozhraní a mikrofonu.

**Vyřešeno:** tablet = Android; PC připojené kabelem (Wake-on-LAN); TV má Screen Mirroring i AllShare.
