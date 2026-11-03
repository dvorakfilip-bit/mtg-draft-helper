# M1 – Datová vrstva

*Datum: 2026-11-03*

## Co je hotové

| Část | Soubor | Popis |
|---|---|---|
| Hledání Areny | `src/MtgDraftHelper.Core/Cards/ArenaPaths.cs` | Cesta k instalaci z `Player.log` (řádek `Mono path`) nebo výchozí cesty. Najde všechny `Raw_CardDatabase_*.mtga`, nejnovější první. |
| Databáze karet | `Cards/ArenaCardDatabase.cs`, `Cards/CardInfo.cs` | Načte karty (jméno, edice, rarita, barvy, mana cost, typy, P/T, text schopností, propojené strany). Novější databáze má přednost, starší doplní chybějící karty. HTML značky se z textů odstraňují. |
| Mana cost | `Cards/ManaCost.cs` | Převod `o3oRoRoGoG` → `{3}{R}{R}{G}{G}`, mana value, barvy, včetně hybridů a phyrexian many. |
| CSV | `Csv/CsvReader.cs` | Proudová čtečka CSV bez alokací pro číselná pole (datasety mají ~600 sloupců a miliony řádků). |
| Stažení datasetů | `Ratings/PublicDatasetClient.cs` | Veřejné datasety 17Lands ze S3. Kontrola verze přes `Last-Modified`, stažení přes dočasný soubor. |
| Herní statistiky | `Ratings/GameDataAggregator.cs` | GP, OH, GD, GIH, GNS WR a IWD pro všechny balíčky i podle hlavních barev balíčku; win rate barevných kombinací. |
| Draftové statistiky | `Ratings/DraftDataAggregator.cs` | ALSA a ATA, včetně druhého picku v Pick-Two Draftu. |
| Skupiny hráčů | `Ratings/RatingsDefaults.cs` | „Top hráči“ = `user_game_win_rate_bucket` ≥ 0,60 (cca nejlepší čtvrtina her). Záložní pořadí formátů podle PRD 5.4. |
| Úložiště | `Storage/StatsStore.cs` | SQLite `%LOCALAPPDATA%\MtgDraftHelper\stats.db`. Ukládají se surové počty, win rate se počítají při čtení (umožní pozdější vyhlazení). |
| Orchestrace | `Ratings/RatingsService.cs` | Zjistí verzi → při změně stáhne → zpracuje → uloží → smaže stažený soubor. Výběr formátu se zálohou. |
| Vývojářský nástroj | `src/MtgDraftHelper.Cli` | Příkazy `cards`, `import`, `top`, `colors`, `match` pro ověření. |
| Testy | `tests/MtgDraftHelper.Core.Tests` | 30 unit testů (CSV, mana cost, agregace, úložiště, čtení databáze karet na syntetické SQLite). |

## Ověření na skutečných datech (FRA Premier Draft)

- Databáze karet: 26 378 karet ze 2 souborů, načtení cca 6 s.
- Import: game data 297 964 her (70 s), draft data 2 374 082 picků (200 s), Debug build.
- Opakovaný import pozná nezměněný dataset (`UpToDate`, < 1 s). Neexistující dataset (FRA Quick Draft) hlásí `NotAvailable`.
- Párování jmen: všech 295 karet z dat 17Lands má protějšek v databázi Areny.
- Porovnání GIH WR s webem 17Lands (web = všechna data dosud, my = dataset k 19. 10.):

| Karta | Web 17Lands | Spočítáno |
|---|---|---|
| Ajani Unrelenting | 73,2 % | 74,0 % |
| Uldaros Theorix | 68,8 % | 69,5 % |
| The Theorist, Jace Beleren | 67,7 % | 68,4 % |
| Sublime Epiphany | 66,5 % | 67,9 % |
| Garruk, Veiled Butcher | 65,6 % | 65,9 % |
| Garruk, Curse Breaker | 65,4 % | 65,4 % |

Rozdíly do 1,5 p. b. odpovídají rozdílnému období dat. Pořadí karet sedí.

## Známá omezení a návrhy na později

1. **Rychlost importu** (cca 4,5 min na edici a formát). Běží jednou při nové verzi datasetu na pozadí, pro MVP stačí. Případně později zrychlit (Release build, paralelní zpracování).
2. **Načítání databáze karet** (cca 6 s) načítá všechny texty. Při startu aplikace poběží na pozadí; lze zrychlit cache jen pro potřebné edice.
3. **Tutored karty** (`tutored_*`) se do GIH nezapočítávají – definice 17Lands to neupřesňuje.
4. **Heuristika rolí karet** (removal apod.) zatím není – text schopností je připravený, role přijdou s doporučováním v M3.
5. Scryfall jako záloha databáze karet zatím není implementovaný – lokální databáze Areny pokryla vše.
