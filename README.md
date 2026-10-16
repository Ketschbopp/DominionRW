# DominionRW

Rim World Dominion, a 1.7 RimWorld mod that expands and overhauls diplomacy and inter-faction interaction massively, with many mod compatibilities.

## Status

**M0 (framework)** and **M1 (world simulation)** are in place: faction politics (stability, legitimacy, governments, blocs, riots, coups, revolutions), economy and military from Economics & Demography, territory and vassal outposts from FTV, capitals, opinions between all factions, and AI-to-AI diplomacy (wars, peace, alliances). The Dominion tab shows every faction with a full dossier.

**M2 (news)** adds the news feed: events reach you late, as rumors or confirmed reports, depending on your intel on the factions involved. There is a Dominion research tab, a radio receiver to monitor, false rumors and corrections, colonist mood reactions, and per-category letters or messages. Faction dossiers only show what your intel allows.

**M3 (diplomacy)** adds the Diplomacy and Treaties tabs: gifts, compliments, insults, peace and war, five treaty types, border policy, embargoes, denunciations and tribute, all costing influence earned by your head of state at the new Leadership Desk (wood, steel or gold). AI factions sign treaties with each other, honour or betray defensive pacts, close borders and embargo you when they dislike you, and offer you treaties when they like you. Embargoes and trade agreements change prices and stop caravans.

**M4 (physical diplomacy)** puts diplomacy on the map: send diplomats or your head of state by caravan on missions to other settlements, exchange real hostages, and hold summits at your capital or theirs. Summits are three rounds of dialogue that depend on your leader's skills and the other leader's personality. AI agreements and offers now travel as delegations you can see (with enough intel) and intercept.

**M5 (Rim Council)** adds the council of factions: sessions hosted in turn, delegations that physically travel to attend, motions from members and from you, weighted votes with lobbying, and decrees (sanctions, censure, ceasefires, collective defense, trade standards, diplomatic immunity, a slavery ban) that bind every member, with real consequences for violating them.

**M6 (intelligence)** adds the Intelligence tab: send agents abroad to build networks, gather intelligence, spread propaganda or bribe officials, run counter-intelligence sweeps, and deal with AI factions gathering intelligence on you and on each other. Covert actions complete it: incite riots, stage coups that install a puppet regime, false flags that pin the blame on a third faction, and stealing technology off-map; and in person, from a caravan at one of their settlements, assassinate the leader, sabotage, steal documents or technology, or rescue a captive colonist on the settlement's own map, with guards who raise the alarm if they notice you. AI services sabotage, stage false flags, coups and assassinations too, and saboteurs can strike your colony.

**M7 (war)** gives every war a goal (punitive, conquest, vassalisation, tribute, regime change, liberation) and a war score from battles, occupations, blockades and deaths in combat. AI warbands cross the world map to besiege, blockade and raid, or to reinforce allies; you see them with enough intel or when they come near, and your caravans can intercept them. Victorious warbands occupy settlements, peace terms follow the score, and a side far enough behind capitulates (you are asked). Allies and puppets are called to arms, rivals slide into cold wars with arms races, border skirmishes and proxy wars, and your caravans can blockade enemy settlements. The new War tab shows it all.

**M8 (broadcasting and culture)** adds the Broadcast Center: news, cultural programmes, programmes for a chosen foreign audience, counter-programming and entertainment. Programmes play on your televisions, Vanilla Books Expanded newspapers print Dominion headlines, and cultural influence slowly wins over other factions. **M9 (integrations)** wires in the linked mods: negotiate settlements into vassal outposts through Faction Territories & Vassalage, embargoes drain Economics & Demography stockpiles and unrest drives emigration, and Simple Leadership, VOE, RimMarket, RimToll, RimCourt, IRBM and Pillage & Plunder all feed opinion, intel, stability, economy or council weight. With Visit Settlements (Nina.Goblin's build), visits build intel and ties, theft and vandalism there become diplomatic incidents, colonists on a visit can run covert operations on the spot, and a summit at their capital opens when your head of state visits it. **M10 (ending and world events)** adds the unification ending: research Planetary Federation, pass the Federation Charter at the Rim Council, and bring every major faction into your Federation (or make them vassals, or defeat them), tracked in the new Federation tab. Factions also live through world events (scandals, famines, plagues that spread, disasters, trade booms, festivals and more), and you can send caravans with aid to factions in crisis. See [docs/DESIGN.md](docs/DESIGN.md) for the architecture, feature spec, integration plan and roadmap.

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

Enable dev mode, then use the dev menu category **Dominion**: raise any event (including coups, revolutions and riots), force a relation between any two factions, set a faction's stability, run a simulated day, log wars, set a war score, force a capitulation, launch a warband, start a cold war, finish operations, log faction states, the scheduler and the event log, reveal all intel or set intel per faction, deliver pending news, spread a false rumor, fill influence, sign any treaty between any two factions, force a summit, dispatch a delegation, announce or open a council session, run the integration self-test, and read the profiler. The **Dominion** tab shows factions, the event log and which integrations are active.
