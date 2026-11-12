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
Dvě rovnocenné varianty vzhledu, přepínač **„Vzhled: Kniha / Fotka přes celou plochu“** (volba se pamatuje):

1. **Kniha** – otevřená dvoustrana: vlevo fotka v bílém rámečku, vpravo datum, popisek knižním písmem, název alba a číslo strany. Úvodní strana s titulní fotkou, názvem, obdobím a úvodním textem alba.
2. **Fotka přes celou plochu** – fotka vyplní celou plochu (černé pozadí, poměr stran zachován), popisek je dole v **poloprůhledném obdélníku** (rozmazané pozadí, světlé knižní písmo; nahoře datum, album, číslo strany). Tlačítko **Skrýt / Ukázat popisek** a **Celá obrazovka** (bez lišt aplikace; ukončení tlačítkem nebo Esc). Úvodní strana: titulní fotka + název a úvodní text v obdélníku.

Společné pro obě varianty:
- Listování velkými šipkami, klepnutím na levou/pravou polovinu, klávesami ← → a mezerníkem.
- **Přehrávat samo** (otočení po ~5 s), **Jen fotky s popiskem** (výchozí zapnuto).
- **Pustit na televizi** (viz 2.3.1) – TV ukazuje stejnou stránku; pro TV se snímek skládá ve variantě „fotka přes celou plochu“.
- PDF / fotokniha zatím **mimo rozsah**.

### 2.3.1 Promítání na televizi (plánováno)
TV: **Samsung UE46F5400** (rok 2013, platforma před Tizenem). **Ověřeno: TV má „Screen Mirroring“ i AllShare (DLNA).** **Bez instalace aplikace do TV a bez převodu do MP4** – obraz se posílá živě z tabletu nebo PC po domácí Wi-Fi.

- **Hlavní cesta – „Pustit na televizi“ přes DLNA:** TV funguje jako DLNA přehrávač (Samsung AllShare). Snímky TV posílá **PC aplikace**; ovládat (listovat) jde z PC i z tabletu. PC TV najde v síti a posílá jí jednotlivé „snímky“: fotka + popisek pěkným písmem, složené v rozlišení 1920×1080 (originály se nemění, snímek je dočasný). Děda listuje na tabletu, TV ukazuje. Volitelně automatické přehrávání.
- **Druhá rovnocenná cesta – zrcadlení obrazovky (Screen Mirroring / Miracast)** z tabletu s Androidem (tablet bude Android) (u Samsung Galaxy Tab funkce Smart View). Na TV se zobrazí čtecí režim přesně jako na tabletu. iPad tuto TV zrcadlit neumí (AirPlay TV z roku 2013 nepodporuje).
- Obě cesty vyžadují, aby tablet / PC a TV byly ve **stejné domácí Wi-Fi**. Nic se neposílá do cloudu.
- **Ověřit na skutečné TV:** v menu Zdroj → „Screen Mirroring“ a v nastavení sítě „AllShare / Nastavení AllShare“ – podpora DLNA přehrávání. Jakmile bude k dispozici, otestovat prototypem.

### 2.3.2 Fotoalbum jako DLNA server pro televizi (plánováno, zatím neimplementovat)
Fotoalbum na PC se v domácí síti ohlásí jako **DLNA/UPnP mediální server**; TV (Samsung UE46F5400, AllShare) ho najde mezi zdroji a děda listuje **jen dálkovým ovladačem TV** – bez tabletu.

- **Popisky:** prohlížeč fotek v TV popisky z EXIF nezobrazuje (nanejvýš název položky po stisku Info). Proto server posílá **hotové snímky 1920×1080 s vykresleným popiskem** – stejný vzhled jako čtecí varianta „fotka přes celou plochu“ (poloprůhledný obdélník dole, velké písmo, datum). Originály se nemění; snímky se tvoří na požádání a ukládají do mezipaměti (při změně popisku se obnoví).
- **Struktura na TV:** „Fotoalbum (PC dědy)“ → alba podle složek (např. „2026 léto“, „Afrika 1998 · Keňa“) → fotky podle data pořízení; volitelně složka „Naposledy přidané“. Výběr, zda ukazovat všechny fotky, nebo jen s popiskem – **k rozhodnutí**.
- **Videa** z karty (MP4) zpřístupnit beze změny; podporu MTS ověřit na TV.
- **Technika:** SSDP (UDP 1900) + popis zařízení + ContentDirectory/ConnectionManager (SOAP), HTTP s hlavičkami DLNA (profil JPEG_LRG); běží ve stejném programu jako Fotoalbum (ikona u hodin). Fotky ze stejného vykreslování jako „Pustit na televizi“ (2.3.1) – oba způsoby mohou existovat vedle sebe.
- **Bezpečnost:** jen domácí síť, jen čtení, žádné změny ani mazání; při prvním spuštění jednorázové povolení ve Firewallu Windows (potvrdí správce).
- **Podmínka:** PC musí běžet (Fotoalbum je u hodin).
- Ověřit až na skutečné TV (zobrazení, rychlost listování, prezentace).

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

### 2.6 Kopírování fotek z karty (součást aplikace)
**Není to samostatný program** – je to součást Fotoalba. Fotoalbum proto běží na pozadí jako ikona u hodin a spouští se s Windows (stejně to bude potřeba pro tablet):
- „Zavřít program“ v okně zavře jen okno (předtím souhrn mazání a uložení); Fotoalbum dál hlídá kartu. Úplné ukončení: menu ikony → „Ukončit Fotoalbum“.
- Nové spuštění Fotoalba (ikona na ploše), dvojklik na ikonu u hodin = otevře okno.
- Menu ikony: Otevřít Fotoalbum, Zkopírovat fotky z karty, Spouštět s Windows, Ukončit.

- **Poznání karty:** disk, který má v kořeni složku `DCIM` a v ní podsložky podle standardu DCF (3 číslice + 5 znaků), např. **`100_PANA`, `101_PANA`** (Panasonic), `100CANON`, `100NIKON`, `100MSDCF` (Sony). Běžný USB disk bez této struktury se ignoruje.
- **Spuštění:** automaticky po zasunutí karty (zpráva Windows o novém disku), při startu počítače se zkontrolují už zasunuté karty; ručně z menu ikony.
- **Co se kopíruje:** fotky JPG a **videa** (MP4, MOV, MTS/M2TS, AVI…), která ještě nejsou v počítači. Videa se hledají v `DCIM/…` a ve složkách `PRIVATE/AVCHD/BDMV/STREAM` (AVCHD, Panasonic/Sony) a `PRIVATE/M4ROOT/CLIP` (Sony) – jen na kartě, která má i DCIM. Ve Fotoalbu (Pracovna) se zatím zobrazují jen fotky. Duplicita se pozná podle **názvu souboru + data a času pořízení** (EXIF u fotek, datum vytvoření u videí; bez data podle velikosti) – funguje i u fotek, ke kterým už byl doplněn popisek. Fotky jednou zkopírované se podruhé nekopírují, ani když je uživatel v PC smazal (evidence `import.db`).
- **Kam:** do složky s fotkami, podsložka **„rok období“** podle data pořízení: jaro (3–5), léto (6–8), podzim (9–11), zima (12–2); např. „2026 léto“. **Zima nese rok, ve kterém začala:** prosinec 2026, leden a únor 2027 → „2026 zima“ (fotky jdou za sebou podle času; složky „2026 jaro / léto / podzim / zima“ se tak i abecedně řadí chronologicky).
- **Karta se nemění** – fotky se kopírují, nic se z ní nemaže. Kopie se ověří (velikost), zachová se datum souboru. Jiná fotka se stejným názvem dostane název `P1000001 (2).JPG`.
- **Bez interakce:** malé okno vpravo dole ukazuje průběh („45 z 312 · P1000045.JPG do složky „2026 léto““), tlačítko Zastavit. Po dokončení „Hotovo – zkopírováno N fotek … Kartu můžete vyjmout.“, okno se samo zavře po 30 s; při chybě zůstane otevřené.
- Zapnutí/vypnutí v nastavení Fotoalba (Ctrl+Alt+S); záznam o kopírování v `%LOCALAPPDATA%\Fotoalbum\import.log`.
- Karta se vkládá do **čtečky karet** (disk s písmenem). Fotoaparát připojený kabelem v režimu MTP se nepodporuje; RAW soubory se nekopírují.

### 2.7 Úpravy po prvním testu ve Windows (schváleno 12. 11. 2026)
Mají přednost před starším textem této specifikace, kde se liší.

**Ikona:** vlastní moderní ikona, motiv **fotka v rámečku s perem** (popisování), barvy aplikace (tmavě zelená + světlá). Platí pro okno, lištu, plochu i ikonu u hodin.

**Druh složky – „Dovolená“ / běžné:**
- Druh se nastavuje **jen celé složce**, ne jednotlivým fotkám (z menu Více mizí „Označit jako cestovní/běžnou“).
- Štítek ve stromu i přepínači se jmenuje **„Dovolená“**; běžné složky **štítek nemají**.
- **Výchozí druh je běžná** (i pro složky vytvořené kopírováním z karty, např. „2026 léto“); dovolenou označí uživatel.
- Složka dovolené je na stejné úrovni jako sezónní složky, např. „2025 Turecko“ vedle „2025 léto“.
- Filtr **„Ukázat jen dovolené“** schová ve stromu běžné složky; uvnitř složky se podle druhu nefiltruje.
- *Smíšená karta* (běžné fotky → dovolená → běžné): zatím se nepředpokládá. Návrh řešení na později: v Pracovně „Přesunout vybrané fotky do nové dovolené“ (rozsah od–do → nová složka např. „2025 Turecko“, označená jako Dovolená).

**Ukládání a Zpět:**
- Psaní se **neukládá průběžně** a jednotlivá slova nejsou kroky historie. Popisek se uloží **při kliknutí na jakékoli jiné tlačítko** (Další, Předchozí, jiná fotka/složka, Bez popisku, zavření…).
- **Zpět / Znovu** vrací celé akce: přechod na jinou fotku, uložený popisek (celý text najednou), Bez popisku, otočení, označení ke smazání, přepsání textu AI korekturou (etapa 2).
  Příklad: napíšu text → Další → *Zpět* vrátí na předchozí fotku → *Zpět* smaže celý napsaný text.
- Historie platí **jen do zavření okna** a **jen v rámci aktuální složky** (nejde přes jinou složku).

**Tlačítko „Bez popisku“:**
- Je **trvale vidět** mezi hlavními tlačítky (ne ve Více); klik fotku označí a **hned přejde na další**.
- Má-li fotka popisek, nejdřív se zeptá (ochrana před omylem).
- Tlačítka rozmístit s dostatečnými mezerami, aby se omylem nekliklo na sousední.

**Smazat popisek:** tlačítko je přímo v rámečku s popiskem.

**Označení ke smazání** (zůstává ve Více):
- Po označení se **hned přejde na další fotku**.
- Na označené fotce je jen **klidný nápis přes fotku** „Bude smazána při zavření“ a malý odkaz „vrátit“ – nesmí vypadat jako dialog, který čeká na kliknutí.

**Souhrn při zavření:** místo názvů souborů **malé náhledy** fotek ke smazání a **název alba** u každé.

**Název fotky (nadpis):**
- Windows zobrazuje popisek v polích *Název* i *Předmět* (čte stejné pole). Nově:
  - **Název** = krátký nadpis (např. „Západ slunce nad Masai Marou“) – vytvoří AI při korektuře; u ručního textu první věta. Zápis: XMP `dc:title`, `XPTitle`.
  - **Předmět / Komentáře / popis** = celý popisek (XMP `dc:description`, IPTC `Caption-Abstract`, EXIF `ImageDescription`, `XPSubject`, `XPComment`).
- Ve Fotoalbu se nadpis zobrazuje **v záhlaví fotky místo názvu souboru JPG**, jakmile existuje.
- Pozor na limit IPTC (~2000 bajtů ≈ 1000 znaků s diakritikou); delší text zůstává celý v XMP.

**Korektura (etapa 2):**
- Odchod z fotky s napsaným/nadiktovaným textem bez korektury → dotaz „Odejít bez korektury?“ – **vždy** (i u běžných fotek, bez volby „neptat se“).
- Fotka s ponechaným ručním textem dostane **vlastní značku „jen ruční text“** (miniatura i pruh postupu); je to povolený stav.

**Servisní složky ve složce s fotkami** (ve stromu se nezobrazují):
- databáze a zálohy: **„Fotoalbum – databáze (nemazat)“** (dříve `_fotoalbum`),
- originály před odstraněním vepsaného textu (etapa 4): **„Fotoalbum – originály (nemazat)“** (dříve `_originaly`).

**Ověřeno uživatelem:** zápis popisků do fotek (ExifTool OK), ikona u hodin, kopírování z karty, ostatní funkce etapy 1.

**Stav:** implementováno (kromě bodů pro etapu 2 – korektura, značka „jen ruční text“, nadpis od AI). Nadpis je zatím první věta popisku. Rozepsaný text se pro případ nečekaného zavření drží v okně aplikace (neukládá se do fotky ani do historie) a po dalším otevření se nabídne.

### 2.8 Automatické aktualizace (schváleno 12. 11. 2026)
- Zdroj: **GitHub Releases** repozitáře (veřejné, bez přihlášení). Nástroj **Velopack** (MIT): instalace do profilu uživatele bez práv správce, rozdílové aktualizace, kontrola celistvosti.
- **Dva kanály:**
  - **stabilní** – jen verze schválené správcem („vydej to dědovi“); výchozí, má ho děda,
  - **testovací** – každá nová zkušební verze; **jen pro správce**, přepíná se ve skrytém nastavení (Ctrl+Alt+S → Aktualizace). Z GitHubu se nic ručně nestahuje.
  - Návrat z testovacího na stabilní kanál nainstaluje poslední stabilní verzi.
- **Kontrola:** minutu po spuštění a pak každých 6 hodin; nová verze se **stáhne potichu na pozadí**.
- **Instalace:** jen když se nepracuje – **při příštím startu** programu/počítače, nebo **po zavření okna** (Fotoalbum běží jen u hodin) → po instalaci se samo znovu spustí u hodin. Nikdy uprostřed práce.
- **V záhlaví programu** je vidět aktuální verze (u testovací „· test“). Je-li stažená nová verze: malá nenápadná zpráva „Nová verze X – nainstaluje se po zavření okna“; po aktualizaci krátké oznámení „Fotoalbum bylo aktualizováno na verzi X“.
- Data (nastavení, databáze, náhledy) zůstávají v `%LOCALAPPDATA%\Fotoalbum`; program se instaluje do `%LOCALAPPDATA%\Fotoalbum.App`.
- Vydání: tag `vX.Y.Z-test.N` = testovací (pre-release), `vX.Y.Z` = stabilní. Sestavuje GitHub Actions (instalátor `Setup.exe` + balíčky aktualizací). Jednorázově je potřeba program nainstalovat instalátorem.

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

## 10. Technologie (rozhodnuto v etapě 1)

- **.NET 10 (LTS)**, C#.
- **Fotoalbum.Core** – knihovna fotek, databáze **SQLite** (Microsoft.Data.Sqlite), zápis/čtení metadat **ExifTool** (argumenty přes UTF-8 soubor, diakritika v cestách i textech), náhledy **SkiaSharp** (MIT; respektuje EXIF orientaci).
- **Fotoalbum.Server** – ASP.NET Core minimal API + webové rozhraní (HTML/CSS/JS bez frameworku, přibalené v knihovně). Poslouchá jen na `127.0.0.1`, náhodný port, jednorázový klíč v cookie. Pro tablet se později zapne poslech v domácí síti s párováním (kap. 9).
- **Fotoalbum.Desktop** – WinForms okno s **WebView2**, koš Windows, výběr složky dialogem, jedna instance, zavření přes souhrn mazání v rozhraní.
- Sestavení: GitHub Actions – testy na Linuxu, samostatný balíček pro Windows (win-x64, bez instalace .NET) + ExifTool.
- Popisek se zapisuje do XMP `dc:description`, IPTC `Caption-Abstract` (+ `CodedCharacterSet=UTF8`), EXIF `ImageDescription`. Texty, které do popisu vkládají fotoaparáty (např. „OLYMPUS DIGITAL CAMERA“), se nepovažují za popisek.
- Ukládání: databáze okamžitě, do fotky ~3 s po posledním psaní, při přechodu na jinou fotku a při zavření. Rozpracovaný text (`isDraft`) se do fotky nezapisuje.
- Řazení fotek ve složce: podle data pořízení (EXIF), pak podle názvu.
- Složky začínající `_` nebo `.` a systémové složky Synology (`@eaDir`, `#recycle`) se nezobrazují.
- Otočení fotky se zatím ukládá jen v aplikaci (do souboru se nezapisuje).
- AI: cloudové služby volané z PC (klíče jen na PC, šifrovaně).

### Etapy
1. **MVP – hotovo** (čeká na vyzkoušení ve Windows): strom složek, miniatury, velká fotka, ruční popisek, automatické ukládání do metadat, označení ke smazání, stavy „bez popisku“ a pokračování (2.5), záloha databáze, lokální server + webové rozhraní.
2. Diktování, ✨ Vylepšit, hlasové opravy, historie verzí v rozhraní, slovníček jmen a míst.
3. Knihovna alb (2.1), úvodní texty, vyhledávání.
4. Staré vepsané popisky: přečtení a odstranění (kap. 7).
5. Čtecí režim – obě varianty (2.3), „Pustit na televizi“ (2.3.1) a DLNA server pro TV (2.3.2).
6. Tablet jako druhá obrazovka PC (kap. 9): poslech v domácí síti, párování QR kódem, probuzení PC.

## 11. Otevřené otázky

1. Ukázkové fotky (2–3) s vepsaným textem pro test čtení a odstranění – dodá uživatel soukromě, ne do repozitáře.
2. Výběr konkrétní služby pro přepis řeči a pro inpainting (po testu na ukázkách).
3. Synology Drive u dědy: typ úlohy (synchronizace / zálohování) a zapnuté verzování na NAS.
4. Model Android tabletu (velikost displeje, verze Androidu) – kvůli rozložení rozhraní a mikrofonu.

**Vyřešeno:** tablet = Android; PC připojené kabelem (Wake-on-LAN); TV má Screen Mirroring i AllShare.
