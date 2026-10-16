# DominionRW

Rim World Dominion, a 1.7 RimWorld mod that expands and overhauls diplomacy and inter-faction interaction massively, with many mod compatibilities.

## Status

**M0 (framework)** and **M1 (world simulation)** are in place: faction politics (stability, legitimacy, governments, blocs, riots, coups, revolutions), economy and military from Economics & Demography, territory and vassal outposts from FTV, capitals, opinions between all factions, and AI-to-AI diplomacy (wars, peace, alliances). The Dominion tab shows every faction with a full dossier.

**M2 (news)** adds the news feed: events reach you late, as rumors or confirmed reports, depending on your intel on the factions involved. There is a Dominion research tab, a radio receiver to monitor, false rumors and corrections, colonist mood reactions, and per-category letters or messages. Faction dossiers only show what your intel allows.

**M3 (diplomacy)** adds the Diplomacy and Treaties tabs: gifts, compliments, insults, peace and war, five treaty types, border policy, embargoes, denunciations and tribute, all costing influence earned by your head of state at the new Leadership Desk (wood, steel or gold). AI factions sign treaties with each other, honour or betray defensive pacts, close borders and embargo you when they dislike you, and offer you treaties when they like you. Embargoes and trade agreements change prices and stop caravans.

**M4 (physical diplomacy)** puts diplomacy on the map: send diplomats or your head of state by caravan on missions to other settlements, exchange real hostages, and hold summits at your capital or theirs. Summits are three rounds of dialogue that depend on your leader's skills and the other leader's personality. AI agreements and offers now travel as delegations you can see (with enough intel) and intercept. See [docs/DESIGN.md](docs/DESIGN.md) for the architecture, feature spec, integration plan and roadmap.

## Requirements

- RimWorld 1.7
- [Harmony](https://github.com/pardeike/HarmonyRimWorld)
- [Economics and Demography](https://github.com/helldanpwnz/Economics-and-Demography)
- Faction Territories and Vassalage (which itself needs Map Mode Framework)

Dominion replaces **The News** and is incompatible with it.

## Building

RimWorld 1.7 runs on .NET 10, so you need the **.NET 10 SDK**. No RimWorld install is needed; the reference assemblies come from NuGet.

```
dotnet build Source/Dominion/Dominion.csproj -c Release
```

The DLL is written to `1.7/Assemblies/Dominion.dll` (committed, so the repo folder works as a mod folder as-is).

## Layout

| Path | Contents |
|---|---|
| `About/` | Mod metadata, preview and icon |
| `1.7/` | Assemblies and Defs for RimWorld 1.7 |
| `Languages/` | Translation keys |
| `Source/Dominion/` | C# source |
| `tools/textures/` | Scripts that generate the mod's textures |
| `docs/` | Design document |

## Testing in game

Enable dev mode, then use the dev menu category **Dominion**: raise any event (including coups, revolutions and riots), force a relation between any two factions, set a faction's stability, run a simulated day, log faction states, the scheduler and the event log, reveal all intel or set intel per faction, deliver pending news, spread a false rumor, fill influence, sign any treaty between any two factions, force a summit, dispatch a delegation, run the integration self-test, and read the profiler. The **Dominion** tab shows factions, the event log and which integrations are active.
