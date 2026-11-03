# M0 – Výsledky průzkumu

*Datum: 2026-11-03*

## 1. Shrnutí

| Oblast | Výsledek |
|---|---|
| Formát logu Areny | Zmapovaný ze tří open-source parserů a částečně ověřený na vlastním `Player.log`. Pár bodů zbývá ověřit na skutečném draftu (kap. 3.6). |
| Databáze karet Areny | Ověřeno. Lokální SQLite obsahuje vše potřebné. `grpId` = `mtga_id` v 17Lands. |
| Data 17Lands | **Zásadní zjištění:** API webu 17Lands se mimo 17lands.com používat nesmí. Povolené jsou jen veřejné datasety (CC BY 4.0). Nutné rozhodnutí a úprava PRD (kap. 2). |
| Testovací data | Tři vlastní syntetické logy v `tests/fixtures/logs/` (kap. 5). |

## 2. Data 17Lands – zásadní zjištění

### 2.1 Co platí
- Web používá endpoint `https://www.17lands.com/api/card_data?expansion=&event_type=&time_period=`. Každá odpověď obsahuje poznámku, že data jsou jen pro použití na 17lands.com. Pro jakékoli použití mimo web, veřejné i soukromé, jsou povolené jen veřejné datasety.
- Stránka [usage_guidelines](https://www.17lands.com/usage_guidelines) dále:
  - nedoporučuje automatizované stahování z API bez výslovného svolení. API je chráněné rate limitem a 17Lands nic negarantuje o jeho stabilitě;
  - pro nástroje, které zobrazují jejich data, požaduje embargo: data nové edice až od 12. dne po vydání na Areně (u krátkých speciálních setů 7 dní);
  - vyžaduje viditelné uvedení zdroje „17Lands“ s odkazem. Uvedení nesmí naznačovat, že 17Lands nástroj podporuje.
- Starý endpoint `card_ratings/data` stále odpovídá, ale ignoruje filtry (barvy, skupina hráčů) a vrací neúplná data. Nepoužitelný.
- Hojně používaný open-source nástroj (fork `MTGA_Draft_17Lands`) přesto stahuje z `/api/card_data`. To je v rozporu s pravidly 17Lands.

### 2.2 Veřejné datasety (povolená cesta)
- [17lands.com/public_datasets](https://www.17lands.com/public_datasets), licence CC BY 4.0, soubory na S3:
  - `draft_data_public.<SET>.<FORMAT>.csv.gz`: jeden řádek = jeden pick. Obsah balíčku, pool, pořadí picku, výhry a prohry eventu, rank. Např. FRA Premier: 86 MB.
  - `game_data_public.<SET>.<FORMAT>.csv.gz`: jeden řádek = jedna hra. Balíček, opening hand, líznuté karty, barvy balíčku (`main_colors`, `splash_colors`), výsledek, rank, `user_game_win_rate_bucket` (podle něj jdou filtrovat „top hráči“). Např. FRA Premier: 25 MB.
  - `cards.csv`: mapa `id → name, expansion, rarity, color_identity, mana_value, types`.
- Z těchto dat se dají spočítat všechny metriky z PRD: GIH WR, OH WR, GD WR, IWD, ALSA, ATA, WR podle barevné kombinace i podle skupiny hráčů. Data jsou podrobnější než API.
- **Nevýhody:**
  - Data vycházejí se zpožděním, podle 17Lands draft data cca 2 týdny po vydání edice a game data cca 3 týdny. Aktualizace jsou nepravidelné (FRA vyšla 29. 9., poslední aktualizace dat 19. 10.).
  - Ne pro všechny formáty: FRA má Premier, Trad, PickTwo, Sealed a TradSealed. **Quick Draft u FRA chybí** (u ECL soubor Quick Draftu neexistuje).
  - Stažení a zpracování desítek až stovek MB na edici. Zpracování proběhne jednou lokálně a výsledky se uloží do SQLite, takže při draftu se nic nestahuje.

### 2.3 Návrh řešení
1. **Primární zdroj: veřejné datasety.** Aplikace stáhne game a draft data pro edici a formát, lokálně spočítá statistiky a uloží je. Při každé nové verzi datasetu přepočítá.
2. **Formát bez datasetu** (Quick Draft, Cube, speciální eventy): data jiného formátu téže edice (Premier). Pro Quick Draft to znamená, že ALSA neodráží chování botů, a tím se snižuje přínos vyšší váhy signálů z PRD.
3. **První 2–3 týdny nové edice:** bez dat, jen heuristiky (viz PRD 5.4).
4. **Volitelně:** napsat 17Lands (Discord) a požádat o svolení k API pro osobní nástroj. Pokud ho dají, přidá se API jako zdroj čerstvějších dat. Do té doby API nepoužívat.

**Rozhodnuto (2026-11-03):** jen veřejné datasety (body 1–3), bez žádosti o API. PRD upraveno (kap. 2, 4.3, 4.4, 5.3, 5.4, 7, 8).

## 3. Formát logu Areny

Zdroje: 17Lands klient `rconroy293/mtga-log-client` (GPL-3.0, aktivní), `bstaple1/MTGA_Draft_17Lands` (MIT, zastaralý, 2023) a jeho aktivní fork `unrealities/MTGA_Draft_17Lands` (MIT, 2026). Kód z nich nepřebíráme, používáme jen znalost formátu.

### 3.1 Obecná struktura (ověřeno na vlastním logu)
- Soubor: `%USERPROFILE%\AppData\LocalLow\Wizards Of The Coast\MTGA\Player.log`. Při startu Areny se přepíše a předchozí relace zůstává v `Player-prev.log`.
- Řádek `DETAILED LOGS: ENABLED` / `DISABLED` ukazuje, jestli jsou zapnuté detailní logy (u tebe ENABLED).
- **Požadavek:** jeden řádek `[UnityCrossThreadLogger]==> Metoda {"id":"<uuid>","request":"<JSON jako string>"}`. JSON je zakódovaný dvakrát.
- **Odpověď:** tři řádky:
  ```
  [UnityCrossThreadLogger]03.11.2026 13:11:52
  <== Metoda(<uuid>)
  {...JSON...}
  ```
- **Časová razítka závisí na locale systému.** U tebe je to `dd.MM.yyyy HH:mm:ss`. Parser musí zvládnout víc formátů.
- Arena v roce 2025 odstranila podtržítka z názvů metod (`Event_Join` → `EventJoin`). Parser porovnává názvy normalizované, bez `_`.
- Některé payloady jsou zakódované víckrát (`request` → `Payload`). Parser musí rozbalovat rekurzivně.
- Fork hlásí, že Arena občas zapíše rozbitý (neescapovaný) vnořený JSON. Parser to musí přežít.

### 3.2 Draft s lidmi (Premier, Traditional, Pick-Two, Cube, Contender)
| Zpráva | Obsah |
|---|---|
| `==> EventJoin` | `EventName`, např. `PremierDraft_FRA_20260929`. Jediné místo s názvem eventu, nutné si ho pamatovat. |
| `Draft.Notify {...}` (stejný řádek) | Obsah balíčku: `draftId`, `SelfPack`, `SelfPick` (od 1), `PackCards` jako **string s ID oddělenými čárkou**. |
| `==> EventPlayerDraftMakePick` | Můj pick: `DraftId`, `Pack`, `Pick` (od 1), `GrpIds` (pole, u Pick-Two 2 karty; starší formát `GrpId`). |
| `<== DraftCompleteDraft` | Konec draftu: `InternalEventName`, `DraftId`, `CardPool` (celý pool). |

P1P1 se podle forku od jara 2026 loguje správně. Dřív `Draft.Notify` pro P1P1 chodil pozdě nebo vůbec. Ověřit.

### 3.3 Quick Draft (boti)
| Zpráva | Obsah |
|---|---|
| `==> BotDraftDraftStatus` | Začátek, `EventName`. |
| `<== BotDraftDraftStatus` / `<== BotDraftDraftPick` | `Payload` (zakódovaný JSON): `DraftStatus` (`PickNext`), `PackNumber`, `PickNumber` (**od 0**), `DraftPack` (**ID jako stringy**), `PickedCards` (celý dosavadní pool), `NumCardsToPick`. |
| `==> BotDraftDraftPick` | `PickInfo.CardIds` (stringy), `PackNumber`, `PickNumber` (od 0). |

### 3.4 Sealed a obnova stavu
- `<== EventJoin`: `Course.CardPool` (pole ID), `InternalEventName`, `CurrentModule`.
- `<== EventGetCoursesV2` (při každém startu Areny): `Courses[]` se všemi rozehranými eventy včetně `CardPool`. Podle toho jde obnovit rozpracovaný sealed nebo draft po restartu aplikace. Ověřeno na tvém logu (např. Jump In s 40 kartami).

### 3.5 Balíček
- `==> EventSetDeckV3` (ověřeno): `EventName`, `Deck.MainDeck[]` a `Deck.Sideboard[]` s položkami `{cardId, quantity}`.

### 3.6 Název eventu → formát a edice
- Typický tvar `<Formát>_<SET>_<datum>`, např. `PremierDraft_FRA_20260929`, `QuickDraft_FRA_20261017`, `Sealed_FRA_20260929`, `TradDraft_...`.
- Výjimky: `Trad_Sealed_NEO_...`, `Y26_FRA_Bot_Draft_...` (Quick), `CubeDraft_Powered_...`, `PickTwoDraft_OM1_...`, `ArenaOpen_Day1_...`, `ArenaDirect_OTJ_Sealed_...`, `QualifierPlayIn_...`.
- Edice se ověří proti seznamu edic 17Lands a databázi Areny. Pro neznámé eventy bude potřeba ruční mapování v konfiguraci.

### 3.7 Neověřeno – zkontrolovat při prvním draftu
1. P1P1 v `Draft.Notify` přichází před pickem.
2. Přesná podoba `DraftCompleteDraft` a dalších hodnot `DraftStatus` (konec Quick Draftu).
3. Zda Arena stále posílá `LogBusinessEvents` s `CardsInPack` / `PickGrpId` (záložní zdroj).

### 3.8 Doplnění do PRD
- **Pick-Two Draft:** nový formát (2 karty na pick, 4 hráči). Detekce podle názvu eventu nebo podle 2 ID v picku. Navrhuju přidat do podporovaných formátů.

## 4. Databáze karet Areny (ověřeno)

- Cesta: `C:\Program Files\Wizards of the Coast\MTGA\MTGA_Data\Downloads\Raw\Raw_CardDatabase_<hash>.mtga` (SQLite). Cestu lze zjistit i z logu (řádek `Mono path[0] = ...`).
- Souborů může být víc (u tebe 2). Brát nejnovější, případně procházet všechny.
- Tabulka `Cards`: `GrpId`, `TitleId`, `ExpansionCode`, `DigitalReleaseSet`, `Rarity` (2 common, 3 uncommon, 4 rare, 5 mythic), `Colors` (1–5 = W U B R G), `OldSchoolManaText` (např. `o3oRoRoGoG`), `Types`, `Subtypes`, `Power`, `Toughness`, `AbilityIds`, `LinkedFaceGrpIds`, `IsToken`, `IsPrimaryCard`, `DraftContent`.
- Jména a texty: `Localizations_enUS (LocId, Formatted, Loc)`. **Pozor:** varianta `Formatted=1` obsahuje HTML značky (např. `<nobr>`), je potřeba je odstranit nebo brát `Formatted=0`.
- Texty schopností: `Abilities.TextId` → `Localizations_enUS`. Použije se pro heuristické určení rolí (removal apod.).
- Číselníky: tabulka `Enums (Type, Value, LocId)`.
- `GrpId` odpovídá `mtga_id` v 17Lands. Veřejné datasety ale používají jména karet, mapuje se přes jméno.
- Ve schématu proběhla v roce 2025 změna (`Localizations` → `Localizations_enUS`). Při čtení je potřeba se bránit dalším změnám.
- Záloha: Scryfall `POST /cards/collection` s `arena_id`.

## 5. Testovací data

V `tests/fixtures/logs/` jsou tři **syntetické** logy. Napsal jsem je sám podle zjištěného formátu, s reálnými ID karet FRA a vymyšlenými UUID:
- `premier_draft.log`: EventJoin, 3× Draft.Notify a pick, DraftCompleteDraft, EventSetDeckV3.
- `quick_draft.log`: EventJoin, BotDraftDraftStatus, 2× pick s odpovědí.
- `sealed_join.log`: EventJoin s poolem 45 karet, EventGetCoursesV2.

Až proběhne první skutečný draft, doplní se anonymizované výňatky ze skutečného logu.

## 6. Dopad na milníky

- M1 (datová vrstva) se rozšíří o stažení a zpracování veřejných datasetů (parsování CSV s cca 700 sloupci, agregace). Je to větší než původně plánované volání API.
- Ostatní milníky beze změny.
