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
- Maketa: https://claude.ai/artifact/WzmcgPRpu6q2vw9fHcCVfy (soukromá, ukázková data).

### 2.3 Čtecí režim
- Celá obrazovka, vzhled listování albem (fotka + popisek), ovládání šipkami.
- Konkrétní vzhled se ještě doladí. PDF / fotokniha zatím **mimo rozsah**.

### 2.3.1 Promítání na televizi (plánováno)
TV: **Samsung UE46F5400** (rok 2013, platforma před Tizenem). **Bez instalace aplikace do TV a bez převodu do MP4** – obraz se posílá živě z tabletu nebo PC po domácí Wi-Fi.

- **Hlavní cesta – „Pustit na televizi“ přes DLNA:** TV funguje jako DLNA přehrávač (Samsung AllShare). Aplikace na tabletu / PC TV najde v síti a posílá jí jednotlivé „snímky“: fotka + popisek pěkným písmem, složené v rozlišení 1920×1080 (originály se nemění, snímek je dočasný). Děda listuje na tabletu, TV ukazuje. Volitelně automatické přehrávání.
- **Záložní cesta – zrcadlení obrazovky (Screen Mirroring / Miracast)** z tabletu s Androidem (u Samsung Galaxy Tab funkce Smart View). Na TV se zobrazí čtecí režim přesně jako na tabletu. iPad tuto TV zrcadlit neumí (AirPlay TV z roku 2013 nepodporuje).
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

## 9. Tablet a synchronizace (plánováno)

Verze pro **PC i tablet**, které **běží offline** (vše funguje bez sítě) a **synchronizují se**, jakmile je zařízení doma na Wi-Fi. Děda nesmí nic zapínat ani nastavovat – **žádný Tailscale ani VPN na jeho zařízeních**.

### Architektura (doporučeno)
```
 PC (offline aplikace) ──┐   domácí Wi-Fi u dědy   ┌── Tablet (offline aplikace)
                         └──►  Orange Pi – sync  ◄──┘
                               server (vždy zapnutý)
                                     │ Tailscale jen zde (správce: vzdálená správa)
                                     ▼
                              Synology NAS u správce – záloha
```
- **Orange Pi u dědy** = synchronizační uzel, běží pořád, děda o něm neví. Drží databázi (popisky, diktáty, historie, stavy, poslední pozice), náhledy fotek a kopii originálů.
- **PC a tablet** mají vlastní lokální databázi a pracují samostatně. Server najdou v domácí síti automaticky (mDNS / pevná adresa), synchronizace běží na pozadí. Bez sítě se změny frontují a odešlou později.
- **Tailscale jen na Orange Pi** – pro správce (vzdálená správa, aktualizace) a pro noční **zálohu na Synology NAS** správce. Pro dědu neviditelné.
- Mimo domov (chalupa, bez Wi-Fi) se pracuje offline a synchronizuje po návratu.

### Fotky na zařízeních
- **Originály**: na PC (hlavní kopie) + záloha na Orange Pi → NAS.
- **Tablet**: zmenšené kopie (např. 2560 px, ~0,5 MB) – stáhne si je ze serveru, celé roky fotek se vejdou. Volba „připravit složku offline“ a automaticky poslední otevřené složky.
- Zápis popisku do metadat originálu provádí **PC aplikace** (a server do své kopie). Úpravy z tabletu se do JPG zapíší, jakmile se PC zesynchronizuje.

### Pravidla synchronizace
- Každá změna nese čas a zařízení; synchronizují se jednotlivé změny, ne celý soubor databáze.
- Konflikt (stejná fotka upravena na dvou zařízeních bez synchronizace): vyhraje novější, **starší verze zůstane v historii** a fotka dostane značku „zkontrolovat“. Nic se tiše neztratí.
- Poslední pozice („Pokračovat…“) se synchronizuje, takže jde přejít z PC na tablet.

### API klíče
- AI volání jdou **přes server na Orange Pi** (klíče jen tam, ne na tabletu). Bez spojení se serverem: diktát se uloží a vylepšení proběhne po synchronizaci, případně přímé volání z PC s klíčem uloženým šifrovaně (rozhodne se v implementaci).

## 10. Technologie (návrh)

- **Klienti PC + tablet:** jedna multiplatformní aplikace s lokální databází (SQLite). Kandidáti: **Flutter** nebo **.NET MAUI** (Windows + Android, případně iPad). Rozhodne se podle volby tabletu.
- **Server na Orange Pi:** malá služba v Dockeru (Linux ARM64) – synchronizační API, SQLite/PostgreSQL, generování náhledů, ExifTool, proxy pro AI.
- **TV:** DLNA odesílání snímků z aplikace (UPnP AV), záložně systémové zrcadlení.
- Cloudové AI služby přes HTTPS.

## 11. Otevřené otázky

1. Ukázkové fotky (2–3) s vepsaným textem pro test čtení a odstranění – dodá uživatel soukromě, ne do repozitáře.
2. Výběr konkrétní služby pro přepis řeči a pro inpainting (po testu na ukázkách).
3. Vzhled čtecího režimu.
4. Tablet: Android (doporučeno kvůli zrcadlení na TV), nebo iPad? Model / velikost?
5. Orange Pi: přesný model, bude fyzicky u dědy? Má disk pro kopii fotek (kolik GB fotek celkem)?
6. Fotky dnes leží na disku dědova PC?
7. TV UE46F5400: ověřit v menu „Screen Mirroring“ a nastavení AllShare / DLNA.
