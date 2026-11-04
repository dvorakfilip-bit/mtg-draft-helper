# PRD – MtG Arena Draft Helper

*Verze: 0.1 (návrh k odsouhlasení) · Datum: 2026-11-02*

## 1. Cíl

Desktopová aplikace pro Windows, která běží současně s MtG Arena a v overlay panelu:
1. během draftu doporučuje, kterou kartu z aktuálního balíčku vzít,
2. po draftu (a v Sealedu) navrhuje složení balíčku.

Doporučení vychází ze statistik 17Lands a z aktuálního stavu draftu (dosavadní picky, barvy, křivka, signály).

Cílový uživatel: zatím jen autor (osobní použití). Případná veřejná distribuce a monetizace až v další fázi.

## 2. Rozsah

### V rozsahu první verze (MVP)
- Všechny limited formáty v Areně: Premier Draft, Quick Draft, Traditional Draft, Pick-Two Draft (2 karty na pick, 4 hráči), Sealed, Traditional Sealed, Cube a speciální eventy (u formátů bez dat 17Lands omezená kvalita doporučení, viz 5.4).
- Doporučení picků s hodnocením 0–100 (100 = nejlepší).
- Doporučení balíčku po draftu / pro Sealed pool.
- Postranní overlay panel přes hru.
- UI v češtině.

### Mimo rozsah MVP (možná rozšíření)
- Odznaky se skóre přímo na kartách v Areně.
- Anglické UI.
- Ruční úprava rolí karet a vlastní hodnocení karet (přepsání skóre 17Lands).
- Constructed formáty, sledování zápasů, statistiky vlastních výsledků.
- macOS.
- Veřejná distribuce, auto-update, monetizace.

## 3. Uživatelské scénáře

1. **Draft:** Spustím aplikaci (nebo běží v pozadí), otevřu v Areně draft. Panel se sám zobrazí, ukáže karty aktuálního balíčku seřazené podle skóre a stav mého poolu. Po každém picku se automaticky aktualizuje.
2. **Skládání balíčku:** Po posledním picku (nebo po otevření Sealed poolu) se panel přepne do režimu balíčku a nabídne 1–3 varianty (barvy, 23 karet, rozdělení landů).
3. **Pokračování:** Zavřu Arenu uprostřed draftu a znovu ji otevřu. Aplikace stav draftu obnoví z logu.
4. **Ovládání:** Panel skryji klávesovou zkratkou, posunu ho myší, zapnu click-through, aby nepřekážel.

## 4. Funkční požadavky

### 4.1 Čtení logu Areny
- Sledovat `%USERPROFILE%\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log` v reálném čase (tail), zvládnout přepsání souboru při restartu Areny.
- Rozpoznat: začátek eventu (formát, edice), obsah balíčku k picku, provedený pick, číslo balíčku/picku, Sealed pool, konec draftu, případně uložený balíček.
- Podporovat rozdílné zprávy pro draft s lidmi (Premier/Traditional) a s boty (Quick).
- Při startu aplikace projít existující log a obnovit rozpracovaný draft.
- Kontrolovat, že jsou zapnuté *Detailed Logs*; pokud ne, zobrazit návod.
- Parser postavit tak, aby šel snadno upravit při změně formátu logu (oddělené rozpoznávání zpráv, testy na ukázkových datech).

Pozn.: Přesný formát zpráv se převezme z open-source parserů (17Lands `mtga-log-client`, MTGA_Draft_17Lands) a ověří na prvním skutečném draftu.

### 4.2 Databáze karet
- Převod `grpId` z logu na kartu: jméno, mana cost, barvy, typ, rarita, edice.
- Primární zdroj: lokální databáze Areny (SQLite soubor `Raw_CardDatabase_*.mtga` v instalaci Areny) – offline, vždy aktuální k verzi hry.
- Záložní zdroj: Scryfall (pole `arena_id`).
- Doplňková klasifikace rolí karet (removal, combat trick, card draw, mana fixing, evasion) – heuristicky z textu karty, později případně ručně upravitelné.

### 4.3 Data 17Lands
- **Zdroj: pouze veřejné datasety 17Lands** ([public_datasets](https://www.17lands.com/public_datasets), licence CC BY 4.0). API webu 17Lands se nepoužívá, protože jeho použití mimo 17lands.com 17Lands nepovoluje (viz `docs/M0-pruzkum.md`).
- Soubory pro edici a formát:
  - `game_data_public.<SET>.<FORMAT>.csv.gz` (jedna hra na řádek): GIH WR, OH WR, GD WR, GP WR, IWD, WR podle barevné kombinace, WR karty v archetypu.
  - `draft_data_public.<SET>.<FORMAT>.csv.gz` (jeden pick na řádek): ALSA, ATA.
  - `cards.csv`: mapa ID → jméno karty.
- Aplikace soubory stáhne, zpracuje proudově (bez načítání celého souboru do paměti) a výsledné statistiky uloží do lokální SQLite. Při draftu se nic nestahuje.
- Kontrola nové verze datasetu (hlavička `Last-Modified`) max. 1× denně nebo na vyžádání. Při změně přepočet na pozadí.
- Filtr skupiny hráčů (všichni / top hráči) podle sloupce `user_game_win_rate_bucket`, výchozí „všichni“. Statistiky se předpočítají pro obě skupiny.
- Uvádět „Data: 17Lands“ s odkazem viditelně v UI, ne skrytě. Uvedení nesmí naznačovat podporu ze strany 17Lands.
- Omezení zdroje: datasety vycházejí cca 2–3 týdny po vydání edice a nepravidelně se aktualizují; ne pro všechny formáty existují (např. Quick Draft).

### 4.4 Výpočet skóre picku (0–100)
Skóre se skládá z několika složek. Váhy budou nastavitelné a odladí se na skutečných draftech.

1. **Základní síla karty:** GIH WR převedený na percentil v rámci edice → 0–100. Při malém počtu her se hodnota stáhne k průměru (Bayesovské vyhlazení).
2. **Soulad s barvami:** bonus/postih podle toho, jak karta sedí do mých barev. Váha roste s číslem picku (v P1 téměř nulová, od poloviny P2 výrazná). Bezbarvé karty a fixing bez postihu.
3. **Archetyp:** pokud mám jasnou barevnou dvojici, zohlednit WR karty v tomto archetypu a sílu archetypu samotného.
4. **Potřeby balíčku:** bonus za roli, která v poolu chybí (removal, creatures na nízké křivce, 2-drops), postih za přebytek (např. 6. drahá karta).
5. **Signály:** pokud chodí pozdě silné karty jedné barvy (pick vs. ALSA), zvýhodnit tuto barvu v rané fázi.
   - **Quick Draft (boti):** boti draftují konzistentně a nic neberou naschvál, signály jsou proto spolehlivější. Váha signálů je vyšší a uplatňuje se déle do draftu. Pro ALSA/ATA se použijí data přímo z Quick Draftu, protože odrážejí chování botů. Pokud dataset Quick Draftu neexistuje, použije se Premier Draft a signály se v Quick Draftu nezvýhodňují tolik.
   - **Draft s lidmi:** signály mají nižší váhu, hlavně v P1 a začátku P2.

U každé karty zobrazit krátké zdůvodnění (1–2 hlavní faktory).

### 4.5 Doporučení balíčku
- Pro každou rozumnou barevnou kombinaci (2 barvy + případný splash) sestavit balíček 23 karet + 17 landů (40 karet), u agresivních balíčků případně 16 landů.
- Omezení: počet creatures (orientačně 14–17), křivka many, max. splash (2–3 karty s dobrým fixingem).
- Ohodnotit varianty (průměrné skóre karet + síla archetypu) a nabídnout 1–3 nejlepší.
- Rozdělení základních landů podle mana symbolů.
- Označit karty „na hraně“ – kandidáty na výměnu.

### 4.6 Overlay UI
- Samostatné průhledné okno vždy navrchu, ukotvené vpravo (pozice a velikost se pamatují).
- Panel má záložky **Draft | Pool | Balíček**. Při změně fáze se aktivní záložka přepne sama, ručně lze přepínat kdykoli.
  - **Draft:** stav (edice, formát, pick X/Y), barvy poolu (sloupce síly), mini křivka, počty creatures/removal, signály; seznam karet balíčku seřazený podle skóre, top 3 zvýrazněné, u každé jméno, barva, skóre, GIH WR, ALSA, zdůvodnění.
  - **Pool:** všechny dosud vybrané karty seskupené podle barvy, v rámci barvy podle mana cost, se skóre; filtr podle barvy a role.
  - **Balíček:** varianty balíčků, seznam karet podle křivky, landy, karty na hraně.
- Nečinný stav: malá ikonka / skrytý panel, dokud neběží draft.
- Ovládání: globální klávesová zkratka pro skrytí/zobrazení, přepínač click-through, nastavitelná průhlednost.
- Okno nastavení: filtr hráčů 17Lands, váhy skóre, zobrazované sloupce, ruční obnovení dat, cesta k logu.
- Jména karet v angličtině (dle Areny), ostatní UI česky. Texty UI oddělené do resource souborů kvůli pozdějšímu překladu.

Požadavek na hru: Arena v režimu okno nebo bezrámečkové okno (overlay nefunguje přes exkluzivní fullscreen).

## 5. Nefunkční požadavky

### 5.1 Výkon
- Aktualizace doporučení do 1 s od zapsání picku do logu.
- Paměť do cca 150 MB, zanedbatelné CPU v nečinnosti.

### 5.2 Spolehlivost
- Pád parseru na neznámé zprávě nesmí shodit aplikaci – zalogovat a pokračovat.
- Funkčnost offline s daty z cache.
- Vlastní diagnostický log aplikace.

### 5.3 Soulad s pravidly
- Pouze čtení logu Areny a vlastních souborů hry. Žádné čtení paměti procesu, žádná automatizace kliků ani zásahy do klienta.
- 17Lands: používat jen veřejné datasety (CC BY 4.0), neposílat dotazy na API webu, viditelně uvádět zdroj.

### 5.4 Formáty bez dat
- Pokud pro daný formát neexistuje dataset, použít dataset jiného formátu téže edice (pořadí: Premier → Traditional → Sealed; pro Sealed opačně).
- Pokud nejsou žádná data (Cube, speciální eventy, první 2–3 týdny nové edice), zobrazit upozornění a skórovat jen podle heuristik (rarita, role, mana cost, soulad s barvami) se sníženou spolehlivostí.

## 6. Technické řešení

- **Jazyk a platforma:** C#, .NET 10, WPF, Windows 10/11.
- **Architektura (moduly):**
  - `LogWatcher` – sledování souboru,
  - `LogParser` – převod řádků logu na události,
  - `DraftState` – stav draftu a poolu,
  - `CardDatabase` – karty z databáze Areny a Scryfallu,
  - `RatingsProvider` – data 17Lands a cache,
  - `Recommender` – skóre picků,
  - `DeckBuilder` – návrh balíčku,
  - `UI` – WPF, MVVM.
- **Lokální úložiště:** SQLite v `%LOCALAPPDATA%\MtgDraftHelper\` (cache 17Lands, nastavení, historie draftů).
- **Historie draftů:** ukládat každý draft (formát, edice, datum, každý pick včetně nabízených karet a skóre, výsledný balíček). Slouží k pozdějšímu ladění vah skóre a analýzám.
- **Testy:** xUnit; parser a doporučování testované na ukázkových logech a pevných datech.
- **Spouštění (osobní použití):** Smart App Control ve Windows blokuje nepodepsané `.exe`. Projekty se proto sestavují bez `.exe` (`UseAppHost=false`) a aplikace se spouští přes podepsaný `dotnet.exe` (`dotnet MtgDraftHelper.App.dll`) zástupcem na ploše. Okno konzole se při startu skryje. Podepsání certifikátem se řeší až při veřejné distribuci.

## 7. Závislosti a rizika

| Riziko | Dopad | Opatření |
|---|---|---|
| Veřejné datasety vycházejí se zpožděním a nepravidelně | Na začátku edice bez dat | Heuristiky, upozornění v UI, záložní formát |
| Změna struktury nebo umístění datasetů | Nenačtou se statistiky | Oddělený `RatingsProvider`, kontrola hlaviček CSV, poslední platná data zůstávají v cache |
| WotC změní formát logu | Parser přestane fungovat | Izolovaný parser, testy, rychlá oprava |
| Málo dat na začátku nové edice | Nepřesné skóre | Vyhlazení, zobrazení spolehlivosti |
| Chybí ukázkový log z vlastního draftu | Parser neověřený | Formát z open-source projektů, ověření na prvním draftu |
| Arena ve fullscreenu | Overlay není vidět | Upozornění v aplikaci a v návodu |
| Změna struktury databáze karet Areny | Nenačtou se karty | Záloha přes Scryfall |

## 8. Milníky

1. **M0 – Průzkum:** formát logu z open-source parserů, ukázková data, ověření endpointů 17Lands a databáze karet Areny.
2. **M1 – Datová vrstva:** databáze karet, stažení a zpracování veřejných datasetů 17Lands, lokální úložiště statistik.
3. **M2 – Parser a stav draftu:** sledování logu, události, obnova stavu, testy.
4. **M3 – Doporučení picků a overlay:** skóre, draftový panel.
5. **M4 – Doporučení balíčku:** DeckBuilder, panel balíčku.
6. **M5 – Nastavení a doladění:** okno nastavení, zkratky, click-through.
7. **M6 – Ověření na reálných draftech:** oprava parseru, ladění vah skóre.

## 9. Otevřené otázky

Zatím žádné.
