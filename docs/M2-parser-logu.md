# M2 – Parser logu a stav draftu

*Datum: 2026-11-04*

## Co je hotové

| Část | Soubor (`src/MtgDraftHelper.Core/Logs/`) | Popis |
|---|---|---|
| Čtení souboru | `LogTailer.cs` | Čte nové řádky z `Player.log` (sdílený přístup, Arena soubor drží otevřený). Nedokončený řádek vrátí až po dopsání. Pozná přepsání souboru při startu Areny (zkrácení nebo změna začátku). |
| Skládání zpráv | `LogMessageReader.cs` | Požadavky `==> Metoda {...}`, odpovědi `<== Metoda(id)` + JSON na dalším (nebo stejném) řádku, `Draft.Notify`, `DETAILED LOGS`. Názvy metod normalizované (bez `_` a přípony `V2`/`V3`). Časová razítka v několika formátech (včetně českého `dd.MM.yyyy`). |
| JSON | `LogJson.cs` | Rekurzivní rozbalení vícenásobně zakódovaných `request`/`Payload`, oprava neescapovaného vnořeného JSONu, čtení ID z polí čísel, polí řetězců i CSV řetězce. |
| Události | `LogEvents.cs`, `LogEventParser.cs` | Vstup do eventu, stav eventů (Course/Courses s poolem), balíček k picku, pick (i 2 karty u Pick-Two), pool z Quick Draftu, konec draftu, odeslaný balíček. Pack/pick vždy od 1 (Quick Draft má v logu od 0). |
| Název eventu | `EventInfo.cs` | Formát (Premier, Trad, Quick, Pick-Two, Sealed, Trad Sealed, Cube, ostatní draft/sealed) a edice; převod na formát 17Lands. Constructed eventy se ignorují. |
| Stav | `DraftState.cs` | Event, ID draftu, fáze (Joined → Drafting → DeckBuilding → Playing → Complete), aktuální balíček, historie picků (co bylo nabídnuto a co vybráno), pool, odeslaný balíček. Pool od serveru má přednost před součtem picků. |
| Sledování | `LogMonitor.cs` | Při startu projde celý log (obnova rozehraného draftu z `EventGetCourses`), pak každých 300 ms čte nové řádky. Chyby jedné zprávy nezastaví sledování. Pro UI vydává neměnný `DraftSnapshot`. |
| CLI | `src/MtgDraftHelper.Cli` | `log [cesta] [--events]` – zpracuje log a vypíše stav; `watch [cesta]` – živé sledování. |
| Testy | `tests/MtgDraftHelper.Core.Tests` | 79 testů celkem (49 nových): skládání zpráv, JSON, názvy eventů, přechody stavu, obnova po restartu, přepsání logu, tři syntetické logy z M0. |

## Ověření

- Syntetické logy: Premier Draft (3 picky, konec draftu, odeslaný balíček), Quick Draft (2 picky, čekající balíček P1p3), Sealed (pool 45 karet) – stav odpovídá.
- Tvůj skutečný `Player.log` (12 MB): zpracování za 0,9 s, 17× seznam eventů, 5× vstup do constructed eventu. Správně vyhodnoceno „žádný rozehraný limited event“ (Jump In a constructed se ignorují).
- **Skutečný draft zatím ověřený není.** Až budeš příště draftovat, spusť v terminálu:

```bash
dotnet src/MtgDraftHelper.Cli/bin/Debug/net10.0/MtgDraftHelper.Cli.dll watch
```

  Vypisuje stav při každé změně (balíček, picky, pool). Případné rozdíly opravím podle tvého logu.

## Zjištění: Smart App Control ve Windows

Na tvém počítači je zapnutý **Smart App Control**. Blokuje spouštění nepodepsaných `.exe` souborů, včetně těch, které tu sestavíme (`MtgDraftHelper.Cli.exe` byl zablokován). Spuštění přes `dotnet <soubor>.dll` funguje.

Dopad na M3 (overlay aplikace) – možnosti, které proberem před M3:
1. Spouštět aplikaci přes `dotnet MtgDraftHelper.App.dll` (zástupce na ploše). Nic nestojí, funguje hned.
2. Podepsat aplikaci certifikátem pro podepisování kódu (placený, smysl má až pro distribuci).
3. Vypnout Smart App Control (nedoporučuji – snižuje ochranu počítače).

## Neověřeno / známá omezení

1. Hodnoty `DraftStatus` v Quick Draftu jiné než `PickNext` (konec draftu) – zpracované jako konec draftu, neověřeno.
2. Záložní zdroj P1P1 z `LogBusinessEvents` (`CardsInPack`) není implementovaný – podle M0 Arena od jara 2026 loguje P1P1 v `Draft.Notify`.
3. `Player-prev.log` se nečte. Rozehraný draft po restartu Areny se obnoví z `EventGetCourses` v novém logu (pool ano; historie picků z předchozího běhu ne).
4. Speciální eventy, jejichž název neobsahuje „Draft“ ani „Sealed“ (např. `QualifierPlayIn_...`), se nerozpoznají jako limited.
