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

### 2.2 Pracovna (popisování)
Cílový monitor: 21″, 1920×1080. Rozložení:

```
┌───────────────┬──────────────────────────────────────────────┐
│ Strom složek  │  pás miniatur  ◀ ▢ ▢ ▣ ▢ ▢ ▶      32/120 ✓   │
│ (lze sbalit)  │ ┌──────────────────────────────────────────┐ │
│               │ │               VELKÁ FOTKA                │ │
│               │ └──────────────────────────────────────────┘ │
│               │ ┌─ Popisek ───────────────────── ✓ Uloženo ┐ │
│               │ └──────────────────────────────────────────┘ │
│               │ [◀ Předchozí] [🎤 DIKTOVAT] [✨ Vylepšit] [Další ▶] │
│               │ [↶ Zpět] [↷ Znovu] [Smazat popisek] [⋯ Více]  │
└───────────────┴──────────────────────────────────────────────┘
```

- Po otevření složky se zobrazí miniatury a **první fotka velká**.
- Miniatury mají značku stavu: ✓ popsáno, ✎ rozepsáno, bez značky = prázdné.
- Filtry: „jen nepopsané“, „skrýt běžné fotky“.
- Otočení fotky ↻ (v menu ⋯).
- Popisek je **pod** fotkou.

### 2.3 Čtecí režim
- Celá obrazovka, vzhled listování albem (fotka + popisek), ovládání šipkami.
- Konkrétní vzhled se ještě doladí. PDF / fotokniha zatím **mimo rozsah**.

### 2.4 Nastavení (jen pro správce)
- Kořenová složka s fotkami, AI služby a klíče, styl popisků, slovníček jmen a míst.
- Pro dědu skryté (heslo / klávesová zkratka).

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
- Surový diktát, historie verzí, stavy a katalog alb: lokální databáze aplikace (SQLite) v profilu uživatele.
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

1. AI starý text **přečte** a vloží ho jako výchozí podklad popisku.
2. Text se z obrazu **odstraní** (oříznutím pruhu, nebo domalováním).
3. Originál se vždy zálohuje, uživatel vidí náhled **před / po** a potvrdí.
4. Hromadné zpracování po složkách.

Otevřené otázky – viz kapitola 10.

## 8. Bezpečnost a soukromí

**Repozitář je veřejný – nesmí do něj přijít žádný klíč, heslo ani osobní fotografie.**

- API klíče se ukládají jen na počítači uživatele: **Správce pověření Windows** nebo šifrovaně přes **DPAPI** v `%APPDATA%\Fotoalbum`.
- Konfigurace v repozitáři obsahuje jen ukázku bez hodnot (`*.example.json`).
- `.gitignore` blokuje `.env`, `*.local.json`, `secrets*`, klíče a certifikáty.
- Doporučeno zapnout v GitHubu **Secret scanning + Push protection** (Settings → Code security).
- Testovací fotky v repozitáři jen vlastní / bez osob, nikdy rodinné fotky.
- Fotky se do cloudu posílají jen při akcích, které to vyžadují (čtení / odstranění textu); popisky a diktát se posílají jako text.

## 9. Technologie (návrh)

- .NET 8, C#, WinUI 3 (případně WPF), instalátor s automatickými aktualizacemi.
- SQLite, ExifTool, cloudové AI služby přes HTTPS.
- Připravit na budoucí síťovou složku (Synology NAS) – **synchronizace zatím mimo rozsah**.

### Etapy
1. **MVP**: strom složek, miniatury, velká fotka, ruční popisek, automatické ukládání do metadat, označení ke smazání.
2. Diktování, ✨ Vylepšit, hlasové opravy, historie verzí, slovníček.
3. Knihovna alb, úvodní texty, filtry běžných fotek.
4. Staré vepsané popisky: přečtení a odstranění.
5. Čtecí režim.

## 10. Otevřené otázky

1. Kde je starý vepsaný popisek – na přidaném pruhu, nebo přímo v obraze? Je vždy na stejném místě a stejným písmem?
2. Existují někde originály bez vepsaného textu (záloha, starý disk)?
3. Ukázkové fotky (2–3) pro test čtení a odstranění textu – předat soukromě, ne do repozitáře.
4. Výběr konkrétní služby pro přepis řeči a pro inpainting (po testu).
5. Vzhled čtecího režimu.
