# Dominion: Design Document

**Status:** v0.2, author decisions applied (§10); M0 in progress
**Target:** RimWorld 1.7 (.NET 10 runtime), Harmony 3
**Package ID (proposed):** `ketschbopp.dominion`

Dominion is a geopolitical simulation layer for RimWorld. It replaces the vanilla faction screen with a full diplomacy, economy, intelligence and politics system, and connects to a set of existing mods so that one event (a coup, a missile strike, an embargo) affects all of them.

---

## 1. Design pillars

1. **Everything is one event stream.** Every meaningful thing that happens, from Dominion, vanilla or a linked mod, becomes a `DominionEvent`. The economy, relations, AI memory, intel and news all listen to that one stream. This is what makes the mods feel like one system instead of ten separate ones.
2. **Physical where it matters, abstract where it doesn't.** Leaders really travel to summits and to the Rim Council, operatives really walk into settlements, and warbands really cross the world map. Background world simulation (AI-to-AI trade, distant politics) stays abstract and cheap.
3. **Performance first.** Strategic simulation runs once per in-game day, spread across the day. Nothing runs per tick on pawns or things. See §3.
4. **Two hard requirements, the rest optional.** Economics & Demography (economy and population) and Faction Territories & Vassalage (territory and vassalage) are required and form the backbone. Every other linked mod adds depth when present and is never required (§4).
5. **Use vanilla systems.** Goodwill, `Faction.RelationKindWith`, `GameCondition`, `IncidentWorker`, `WorldObject`, `Caravan`, `Lord`/`LordJob`, `Letter`, `Quest` where suitable, `ResearchProjectDef`, `ThoughtDef`, `Precept`/`Ideo`. Dominion adds state on top of these instead of replacing them.
6. **Everything is configurable** from Mod Settings, and **everything is spawnable** from the Dev menu.

---

## 2. Architecture overview

```
                 ┌───────────────────────── Integrations (optional, reflection-bound) ─────────────────────────┐
                 │ FTV · E&D · VOE · Visit Settlements · Simple Leadership · RimToll · RimCourt · RimMarket ·  │
                 │ IRBM · Custom Base Framework · Vanilla Books Expanded · Pillage & Plunder                    │
                 └───────────────┬───────────────────────────────────────────────────────▲────────────────────┘
                     raise events │                                                       │ apply effects
                                  ▼                                                       │
 vanilla hooks ──► ┌──────────────────────────┐   dispatch   ┌──────────────────────────────┴──┐
 (Harmony)         │       Event Bus          │────────────► │ Listeners                        │
 player actions ─► │  DominionEvent + Def     │              │  Relations · Economy · Stability │
 AI decisions ───► └──────────────────────────┘              │  Intel · News · AI Memory · Ideo │
                                  ▲                          └──────────────────────────────────┘
                                  │ daily, staggered
                     ┌────────────┴─────────────┐
                     │  WorldComponent_Dominion │  FactionState[] · RelationState[,] · Treaties · Decrees
                     │  (the simulation)        │  Operations · Warbands · Delegations · Intel · NewsLog
                     └──────────────────────────┘
```

### 2.1 Core state (saved)

| Type | Holds |
|---|---|
| `WorldComponent_Dominion` | Root of all state. Owns the scheduler and event bus. |
| `FactionState` (per faction) | Stability, legitimacy, war weariness, economy index, military strength, government type, capital settlement, internal blocs, cultural influence map, AI personality, cooldowns. |
| `RelationState` (per ordered faction pair, sparse) | Opinion (separate from goodwill and feeding it), tension, active treaties, border status, embargo or blockade flags, war record, memory of grievances. |
| `Treaty` | Type, parties, terms, start and expiry, violation counter. |
| `Decree` | Rim Council resolution, signatories, compliance per faction. |
| `Operation` | Covert or special op: type, operatives, target, phase, exposure risk. |
| `IntelRecord` (observer → target) | Intel level 0–100, decaying; sets what the player can see. |
| `NewsLog` | Ring buffer of delivered news (size capped in settings). |

Pairwise data is stored **sparsely** (dictionary keyed by `(loadIdA, loadIdB)`), and only for pairs that have something non-default. 30 factions don't cost 900 records.

### 2.2 Event bus

```csharp
public class DominionEventDef : Def {
    public EventCategory category;        // War, Diplomacy, Economy, Politics, Espionage, Disaster, Culture...
    public Secrecy secrecy;               // Public, Restricted, Secret, BlackOps
    public float newsworthiness;          // 0-1; drives news priority
    public List<EventEffect> effects;     // XML-defined default effects (stability, opinion, economy...)
    public RulePackDef headlines;         // vanilla grammar for dynamic text
    public Type workerClass;              // optional C# hook
}
```

- Events are **plain data and dispatched synchronously** to listeners registered at startup. There's no reflection on the hot path.
- Effects are data-driven in XML so they can be tuned or modded without C#.
- News text is **resolved lazily** (grammar runs only when the item is first displayed) to keep the daily cost low.

### 2.3 Source layout (proposed)

```
About/  About.xml, Preview.png
LoadFolders.xml
1.7/
  Assemblies/Dominion.dll
  Defs/ Patches/            core content
Compat/
  FTV/ ED/ VOE/ VisitSettlements/ SimpleLeadership/ RimToll/ RimCourt/
  RimMarket/ IRBM/ CBF/ VBE/ PillageAndPlunder/      XML-only, loaded via IfModActive
Languages/English/Keyed, DefInjected
Textures/
Source/Dominion/
  Core/  Sim/  Events/  Diplomacy/  Council/  Espionage/  Warfare/  Economy/
  Politics/  Culture/  News/  Broadcast/  UI/  AI/  Integrations/  Debug/  Settings/
docs/
```

Build: `Source/Dominion/Dominion.csproj` targets **`net10.0`** (RimWorld 1.7 runs on .NET 10 instead of Unity's Mono; verified against the 1.7 reference assemblies and against linked mods' 1.7 builds). It references `Krafs.Rimworld.Ref` 1.7 and `Lib.Harmony.Ref` 3.x (reference-only; Harmony itself comes from the `brrainz.harmony` mod at runtime). It builds anywhere with the .NET 10 SDK and no RimWorld install, and the DLL is committed to `1.7/Assemblies`.

---

## 3. Performance budget

| Layer | Frequency | Budget |
|---|---|---|
| Strategic sim (politics, economy, AI diplomacy) | Once per day, **staggered**: each faction gets a slot `hash(faction) % 60000` so work is spread over the day | < 0.5 ms per faction slot |
| Movement (warbands, delegations, operatives) | Native `WorldObject.Tick` via vanilla `Tweener`/pather, as caravans do | ≈ vanilla caravan cost |
| Operations, summits, council | Hourly (`2500` ticks) check of active items only | < 0.1 ms |
| Price modifiers | Cached per faction once per day; `Tradeable.GetPriceFor` postfix is one dictionary lookup | O(1) |
| UI | Computed only while a window is open; cached per frame | n/a |

Rules:
- **No patches on per-tick pawn or thing methods** (`Pawn.Tick`, `Thing.Tick`, `JobDriver` loops).
- All lookups by `loadID`; no `Find.FactionManager.AllFactions` LINQ in loops; reuse static lists.
- Integration reflection is resolved **once at startup** into cached delegates (`AccessTools.MethodDelegate` / `FieldRefAccess`).
- Built-in **profiler dev action** reports time per subsystem, so regressions are visible.
- Settings expose "simulation detail" presets (Low / Normal / High) for very large faction counts.

---

## 4. Integration strategy

### 4.1 Mechanism

- **One DLL**, no compile-time dependency on any other mod. Each integration is an `IDominionIntegration` class that activates only if `ModsConfig.IsActive(packageId)`, binds the target mod's members by name through Harmony's `AccessTools`, and fails soft: if a member is missing after an update, it logs one warning, disables itself, and the game continues.
- XML content for an integration (defs, patches) lives in `Compat/<Mod>/` and is loaded by `LoadFolders.xml` with `IfModActive`.
- About.xml lists every linked mod under `loadAfter` (never `modDependencies`).
- Each optional integration can be toggled off individually in settings.
- **Exception: Economics & Demography and Faction Territories & Vassalage are hard requirements.** Both are listed in `modDependencies`. Dominion's economy and population model reads from E&D, and its territory and vassalage model reads from FTV, with no fallbacks of its own. (FTV itself requires NozoMe's Map Mode Framework, so that comes along too.) If either one fails to bind, for example while it has no 1.7 build, Dominion logs an error and runs in a degraded mode (population and wealth estimated from settlement counts, no territory or vassal data) so a save is never broken by it. Binding still goes through cached delegates, so E&D updates are caught by the startup self-test with a clear error instead of a crash.

### 4.2 Version note

As of the reference snapshots, most linked mods ship **1.6 assemblies only**. Custom Base Framework and Pillage & Plunder already ship 1.7. Because 1.7 moved from .NET Framework 4.7.2 to .NET 10, a mod's 1.6 DLL cannot load in 1.7, so each mod needs its own 1.7 build before its integration can activate. **This matters most for E&D and FTV:** as hard requirements, Dominion cannot be played until both ship 1.7 builds (FTV's Map Mode Framework dependency as well). The bindings below target the current public APIs; each one is version-guarded and will be re-checked as the mods move to 1.7.

### 4.3 Per-mod plan

Type names below come from the mods' current public assemblies.

| Mod (packageId) | What Dominion reads | What Dominion drives | Hooks found |
|---|---|---|---|
| **Faction Territories & Vassalage** (`jaeger972.factionterritories`), **required** | Territory ownership per tile, vassal outposts | War outcomes cede settlements; treaties transfer territory; vassal status is synced both ways with Dominion's vassalage; border rights use real territory | `TerritoryOwnershipCache.TryGetClaimingFactions`, `VassaliseUtility.ExecuteVassalisation / ExecuteCedeToFactionAtTile`, `VassaliseComponent`, `Invasions.Utility.TryCreateForSettlement`, `Expansion.SettlementConstruction` |
| **Economics & Demography** (`helldan.economicsdemography`), **required** | Real population, stockpiles, wealth, inflation, global price modifiers | Embargoes and blockades cut stockpile flow; wars, plagues and famine change population; sabotage destroys stockpile wealth; trade deals add flows | `WorldPopulationManager.GetPopulation / ModifyPopulation / GetStockpile / FactionWealth / globalPriceModifiers`, `VirtualStockpile.TryConsumeWealth` |
| **Vanilla Outposts Expanded** (`vanillaexpanded.outposts`) | Player and AI outposts as strategic assets | Outposts project territory and intel; defensive outposts can intercept warbands; artillery outposts can bombard (with diplomatic consequences); production feeds the economy; outposts become targets of AI operations | `VOE.Outpost_*`, `TravellingArtilleryStrike` |
| **Visit Settlements** (`alt4s.VisitSettlements`) | Settlement maps the player is visiting | Physical espionage and special operations happen on the real visited map; theft or destruction there raises Dominion incidents; summits can be held in a visited settlement | `WorldComponent_SettlementData.settlementMaps`, `VS_Penalize*` patches (listen) |
| **Simple Leadership** (`pb3n.SimpleLeadership`) | Settlement leaders, acting leaders, power events (riots, power struggles) | Assassinations kill real leaders; Dominion coups start Simple Leadership power struggles; its riots and successions become Dominion politics events; leaders are the pawns who travel to summits | `WorldComponent_LeaderTracker.GetBaseLeader / StartPowerEvent / GetActiveEventsFor`, `FactionLeadershipData`, `PowerStruggle`, `Rioting` |
| **RimToll** (`forti.rimtoll`) | Toll booths, faction resentment, toll memory | Border closures and embargoes change toll behaviour; resentment feeds Dominion opinion; Dominion warbands and delegations pass booths | `GameComponent_TollState.ResentmentFor / MemoryFor / MostResentfulFaction`, `TollUtility` |
| **RimCourt** (`forti.rimcourt`) | Court cases, judge reputation, exiles, betrothals | Captured operatives and spies can be tried in court; envoy and arbitration petitions come from Dominion diplomacy; betrothals become political marriages between factions | `CourtManager`, `PetitionWorker_Envoy / Arbitration / Pardon`, `Betrothal`, `JudgeReputation` |
| **RimMarket** (`forti.rimmarket`) | Market reputation, trade routes, convoys, wanted offers | Trade deals create real `TradeRoute`s; embargoes suspend routes; blockades and warbands intercept `TradeConvoy` objects; Great Fairs become news | `Routes.TradeRoute / TradeConvoy`, `Reputation.MarketReputation`, `Market.MarketManager / MarketTruce` |
| **InterRim Ballistic Missile** (`kazepsi.irbm`) | Missile silos, satellites, doomsday clock, nuclear winter | Missile strikes are war acts and huge news; Rim Council can pass non-proliferation decrees; recon satellites raise intel; AI missile use follows Dominion war state | `SatelliteRegistry`, `DoomsdayClockManager`, `MissileQueueManager`, `AIFactionMissileUtility`, `IncidentWorker_AIFaction*` |
| **Custom Base Framework** (`EvilEyeGuy.CustomBaseFramework`) | Settlement plan type and capital status | Each faction's **capital** comes from CBF's `Hauptsitze` (seats) when present; settlement type (fort, town, port...) sets its strategic value, garrison and intel difficulty | `Hauptsitze.IstHauptsitz`, `SettlementPlanDef`, `CBFSiteExtension` |
| **Vanilla Books Expanded** (`VanillaExpanded.VBooksE`) | Newspaper items | Newspapers print real Dominion headlines; broadcast-centre programs shape their slant | `Newspaper`, `BookOutcomeDoer_Newspaper` (postfix) |
| **Pillage & Plunder** (`lordfelix.pillageandplunder`) | Plundered settlements, NPC plunder incidents | Plunder is a war act and news item; plundered settlements lose economy and stability | `PlunderedSettlementManager`, `IncidentWorker_NpcSettlementPlunder` |
| **The News** (`xwe1ss.thenews`) | none | Dominion **replaces** it, with the same design reimplemented. It is marked `incompatibleWith` to avoid double news, price patches and coups. | n/a |

Fallbacks when an optional mod is absent (E&D and FTV have none because they are required):
- **Capital:** Largest or oldest settlement, or the leader's home.
- **Leaders:** Vanilla `Faction.leader`.

### 4.4 Cross-mod compatibility

Dominion acts as the **arbiter** where linked mods overlap:
- **Prices:** E&D global price modifiers and Dominion embargo, plague or crisis modifiers are combined in one place, multiplicatively, never applied twice.
- **Riots and coups:** Simple Leadership power events and Dominion coups are the same event. Whichever fires first, the other is notified instead of firing again.
- **Vassalage and territory:** FTV is the source of truth, and Dominion's treaties, wars and puppets act through it.
- **NPC raids and plunder:** FTV invasions, P&P plunder and Dominion warbands share one "military action" event, so news and relations count each once.

---

## 5. Feature modules

### 5.1 Faction states and internal politics

Each faction tracks:
- **Stability** (0–100). Low means riots, then coups, then revolution. **Factions are never split mid-game:** a successful revolution replaces the regime (new leader, government type and bloc balance) inside the same faction, and a rebellious settlement can at most defect to an *existing* faction.
- **Legitimacy** of the leader, **war weariness**, **economy index**, **military strength**.
- **Government type**, derived from ideology memes and royal titles: Autocracy, Theocracy, Council, Tribal Confederacy, Corporate, Hive or Mech. It shapes which actions are possible.
- **Political blocs** (for example Hawks, Doves, Traders, Zealots), each with support %, which players can fund or undermine.
- **Capital:** the leader lives there. Losing it is a severe stability blow and can force capitulation.
- **AI personality**, from leader traits, ideo memes and tech level: aggression, greed, honour, paranoia.

### 5.2 Diplomacy (Faction tab overhaul)

A new main tab, **Dominion**, has sub-tabs:
- **Overview:** world map overlay of territories, alliances, wars and tension.
- **Factions:** dossier per faction with leader photo, government, stability, economy, military and known intel.
- **Diplomacy:** actions with the selected faction.
- **Treaties:** active and proposed treaties.
- **Intelligence:** operations and intel levels.
- **Council:** the Rim Council.
- **News:** the news feed (§5.9).

The vanilla Factions tab remains reachable.

Actions (each with costs, prerequisites and AI acceptance odds shown):
- **Communicate:** gift, compliment, insult, demand tribute, threaten, apologise.
- **Treaties:** non-aggression pact, trade agreement (with RimMarket, a real trade route), alliance, defensive pact, military access, **intel sharing**, **border rights** (open by default; can be closed, restricted or tolled), research cooperation, hostage exchange, political marriage (with RimCourt, a betrothal), guarantee of independence, **joint war** against a third party.
- **Economic:** embargo, sanctions, blockade (needs a naval or warband presence), loans, investment, tribute.
- **Hostile:** denounce, declare cold war (rivalry without war), declare war with a war goal, support rebels, proxy war.
- **Vassalage:** demand vassalage, release vassal, puppet installation (after a coup), annexation.

AI factions run the same action set **among themselves** in the daily simulation, which creates alliances, wars and blocs without the player.

### 5.3 Physical diplomacy: summits, envoys, hostages

- **Envoys:** sending a high-impact proposal sends a real envoy. Players form a caravan, and AI envoys are a `Delegation` world object that moves, can be seen with enough intel, and **can be intercepted**, which causes a diplomatic incident.
- **Peace summits:** both leaders travel to a site: a neutral summit world object, one side's settlement (through Visit Settlements when installed), or the player colony. When both are present, a lord job keeps them at the table, and a **dialogue window** opens:
  - Portraits, a paper-document layout, and branching dialogue whose options depend on the leader's skills, traits, ideology, intel held, and leverage (hostages, war score).
  - Outcomes: treaty terms, ceasefire, territory cession, reparations, hostage exchange, or a walk-out.
  - Things can happen during it: an assassination attempt, a protest, a betrayal.
- **Hostage exchange:** prisoners, and leaders' kin, are bargaining chips. The exchange happens physically on the map at the agreed time.

### 5.4 The Rim Council

- A recurring session (default: once per year, configurable), hosted in rotation by member factions or by the player once eligible.
- **Membership:** all non-hidden humanlike factions **except tribals** (tech level Neolithic or lower; the threshold is configurable). A tribal faction that is the player's vassal sits in through its overlord.
- **No vetoes.** Every vote is weighted, and nobody can block a motion alone. The only exception is a dev-mode tool that lets the player veto, for testing.
- Delegates (leaders or envoys) **physically travel** to the host settlement. Absent members cannot vote.
- When the session opens, the **Council window** shows motions, debate (influence spending) and weighted voting. Vote weight = f(population, economy, military, allies).
- **Decrees**, for example: sanctions on a faction, a ceasefire order, a non-proliferation treaty (IRBM), a plague quarantine, recognition of territory, a trade standard (tariff cap), banning a practice (organ trade, slavery), a peacekeeping coalition, admitting a member, and the **Federation Charter** (for the unification ending).
- Decrees can be **violated**. Violators lose goodwill with every signatory and become eligible for sanctions motions, and violations are news.

### 5.5 Espionage and special operations

`OperationDef`, which is data-driven: required skills, team size, duration, base success, exposure risk and effects.

**The player chooses the mode for each operation:** *abstract* (resolved off-map from the team's skills) or *physical* (played on the real map). Physical is riskier to run but gives a better success chance and better rewards. The Mode column below is the AI's default and marks which ops can only be one or the other.

| Operation | Mode | Effect |
|---|---|---|
| Establish spy network | Abstract | Raises intel over time; base for other ops |
| Gather intel / steal documents | Abstract or physical | Intel spike, reveals warbands, treaties, secret events |
| Counter-intelligence | Abstract | Hunts enemy spies in your colony and allies |
| Assassinate leader | **Physical** | Kills the real leader (Simple Leadership aware); succession crisis |
| Sabotage (blow up depot, power or silo) | **Physical** | Economy or military loss (E&D stockpile, IRBM silo) |
| Incite riot / fund rebels | Abstract | Stability down; may trigger coup |
| Coup / install puppet | Abstract and physical climax | Regime change; puppet vassal |
| False flag | Abstract | Blames a third faction; may start a war |
| Propaganda drop / bribe official | Abstract | Bloc support, opinion |
| Rescue or extract hostage | **Physical** | Recovers a pawn |
| Steal technology | Physical | Research progress |

- **Physical** ops send operatives as a caravan. On arrival, the target settlement map is generated (Visit Settlements if present, otherwise vanilla map generation with an "infiltration" lord so guards are not hostile until alerted). The operatives must reach the target (a leader pawn or a marked building) and get out. Alarm status escalates normally.
- **Exposure:** captured operatives can be interrogated, exposing the operation. That leads to a diplomatic incident, news, possibly war, and, with RimCourt, a trial.
- AI factions run operations too, including against the player (enemy spies, saboteurs), and counter-intel catches them.

### 5.6 War, warbands and territory

- **War goals:** conquest of a settlement, vassalisation, tribute, regime change, liberation, punitive.
- **War score** comes from battles, occupied settlements, blockades and missile strikes. Peace is negotiated in a summit, or forced at capitulation.
- **Warbands:** a `Warband` world object with a real pawn group (composition from faction `PawnGroupMaker`) moving across the world. It is **only rendered for the player when intel on that faction is high enough**, or when it is within range of player assets (outposts, satellites, caravans). The player can **intercept** it (caravan attack generates a battle map, through vanilla caravan-meets-world-object mechanics) or let it pass. Arriving warbands besiege settlements (FTV invasions if present), raid the player, or reinforce allies.
- **Occupation** changes settlement ownership (`Settlement.SetFaction`), so territory (FTV) and economy update.
- **Cold war:** high tension without war leads to arms races, proxy wars, espionage surges, and propaganda battles.
- **Joint wars:** allies can be asked to join. Refusal hurts opinion, and AI-to-AI requests happen too.

### 5.7 Economy

- Faction economy index (computed from E&D's population, stockpiles and wealth) is affected by trade deals, embargoes, blockades, war, plagues, sabotage, vassal tribute and outposts (VOE).
- **Embargo:** that faction's traders refuse you, or charge a markup.
- **Blockade:** caravans and convoys to and from the target are intercepted.
- Price effects at the trade screen come from the cached daily modifier table (§3), with a tooltip that explains every factor ("Embargo +25%", "Plague in region +10%").
- **Plagues:** a world plague event spreads along trade routes and borders. Infected factions lose population and economy, traders from them can bring disease to the player (vanilla disease incident), and quarantine decrees slow it.

### 5.8 Culture and ideology

- **Ideological compatibility** between factions shifts opinion daily (shared memes and precepts help, opposing ones hurt).
- **Cultural influence:** each faction projects influence through broadcasts, trade, vassals and proximity. Its main effect is **political**: it shifts a faction's blocs, opinion and stability. **Ideology is affected to a lesser degree:** sustained, very high influence can trigger an occasional minor reform (one non-core precept changed through vanilla `Ideo`), rate-limited and toggleable in settings. Memes and the ideology itself are never replaced.
- Ideology precepts gate actions; for example, a pacifist ideo can't declare aggressive wars without a stability cost.

### 5.9 News, intel and broadcasting (The News, reimplemented)

- **News feed:** a sub-tab with filters (category, faction, confidence, source), headline and story text from grammar, a confidence level (*Rumor / Confirmed / Disinformation*), and a source (radio intercept, trader gossip, spy report, broadcast, council communique...).
- **Visibility:** an event becomes news for the player only if its `secrecy` is at or below what the player can see. That depends on intel level on the factions involved, news research, receivers built, and broadcasts. Secret events can **leak** later.
- **News tech** (research tree): Radio Monitoring → World Comms Protocols → Signal Decoding → Intelligence Agency → Diplomatic Corps → Satellite Intelligence (IRBM satellites if present) → Planetary Federation (unification).
- **Radio receiver:** a monitored building that raises news coverage, operated as a work type.
- **Broadcast Center:** a powered building with an operator job. It runs **programs**:
  - *News* (colony mood: informed or anxious, based on world state)
  - *Propaganda* targeting a faction (their stability down, your influence up; if traced, an opinion penalty)
  - *Cultural* (spreads your ideo influence)
  - *Counter-propaganda* (blocks enemy programs)
  - *Entertainment* (joy)
  - *Disinformation* (plants false news; high risk)
- **TVs** (vanilla television joy) give a thought tied to the current program. **VBE newspapers** print current Dominion headlines and carry the active program's slant.
- Colony reaction: thoughts for nearby wars, plague news, alliances and so on, as in The News.

### 5.10 Leadership Desk

- A new building, `Dominion_LeadershipDesk`, made from **Wood, Steel or Gold** (`stuffCategories` plus a filter so only those three are allowed).
- **Who is head of state** is a setting: either **the ideology leader** (vanilla `Precept_RoleSingle` leader), or **a pawn chosen at the desk**.
- **Diplomats** work the same way, also as a setting: either pawns holding a **Diplomat ideology role** (a `PreceptDef` that Dominion adds to Ideology), or **pawns chosen at the desk**. Diplomats are the pawns sent as envoys and summit delegates when the head of state does not go.
- If Ideology is not installed, both fall back to choosing at the desk.
- **Function:** the head of state works here to:
  - unlock the full Dominion diplomacy action list (without a desk, only basic comms-console diplomacy is available)
  - draft treaties (reduced cooldowns), prepare council motions, and review intel dossiers
  - **material tier:** Wood is the basic tier; Steel gives faster drafting; Gold gives a prestige bonus to diplomacy acceptance and a room impressiveness boost
- Desk quality and the leader's Social and Intellectual skills scale the bonus.

### 5.11 Capitals and the player's state

- Every faction has a **capital** (§4.3 CBF or a fallback). The player designates a capital colony at the Leadership Desk.
- Capitals have higher defence, higher intel difficulty, and are where leaders reside and where AI-hosted summits and councils take place.

### 5.12 Unification ending

**Goal:** unite every *major* faction (configurable definition: non-hidden, humanlike, with a settlement) under a **Galactic Federation** led by the player.

A faction counts as unified if it is a signed Federation member, a vassal or puppet of the player, or has been eliminated or annexed by the player.

Three paths:
- **Peaceful:** the Federation Charter passes at the Rim Council, then each remaining faction signs through summits.
- **Coerced:** vassalage and puppets.
- **Conquest:** total war.

A progress tracker sits in the Dominion tab. On completion, an ending dialog (vanilla `GameVictoryUtility` style) shows custom text that depends on which path dominated.

### 5.13 Dynamic events

Daily rolls weighted by world state include border skirmishes, defections, scandals, assassination attempts, refugee waves, famine, plague outbreaks, trade booms, succession crises, religious schisms, secession, natural disasters at settlements, council emergencies, and arms-race incidents. Each one is a `DominionEventDef` with conditions and effects, so new events are XML-only. Some of them are **visitable**, as a world site the player can travel to (rallies, ceremonies, disaster relief, signing ceremonies).

---

## 6. UI style

The UI follows **RimToll and RimCourt**: windows look like **paper documents** (sheet background, typed text, ink stamps), and dossiers show a **photo** of the leader with stamped verdicts and seals. Assets mirror their `Sheet`, `Stamp` and `Photo` texture approach, with new art for Dominion seals, treaty paper and newspaper mastheads. **All textures are made in this repo** (generated from scripts in `tools/textures/`, so they can be regenerated and tweaked). All windows are resizable and readable at UI scale 1.0–2.0.

---

## 7. Settings

The settings window is tabbed: General · Simulation · Diplomacy · Council · Espionage · War · Economy · Culture · News & Broadcast · Integrations · Performance · Ending.

Settings include:
- On/off per module and per integration
- Multipliers for every rate (stability drift, AI aggression, op success, intel decay, news volume, plague spread...)
- Council frequency, ending definition, and "simulation detail" preset
- Notification mode per news category (silent / message / letter)

---

## 8. Dev menu

Debug category **Dominion** includes:
- Fire any `DominionEventDef` against chosen factions
- Force war, peace, alliance, vassalage or coup; set stability, opinion, intel or economy
- Spawn a warband, delegation or operative team; convene a summit or the council now; add or remove a decree
- Reveal all intel; deliver all pending news; run the daily sim now
- Dump state; dump the event log; **profile** subsystems
- Per-integration self-test (shows which bindings resolved)

---

## 9. Roadmap

| Milestone | Scope |
|---|---|
| **M0: Skeleton** ✅ | About/LoadFolders, csproj (Krafs ref), Harmony bootstrap, settings shell, dev menu shell, `WorldComponent_Dominion`, scheduler, event bus, integration loader with self-test |
| **M1: World sim** ✅ | FactionState, RelationState, stability, economy index, AI personalities, daily AI diplomacy (abstract), capitals, E&D and FTV core bindings |
| **M2: News** | News feed tab, visibility and intel, grammar headlines, radio, research tree, colony thoughts |
| **M3: Diplomacy UI** | Dominion tab, dossiers, actions, treaties, embargo and border rights, Leadership Desk |
| **M4: Physical diplomacy** | Envoys, delegations, summits plus the dialogue system, hostage exchange |
| **M5: Rim Council** | Sessions, motions, voting, decrees, violations |
| **M6: Espionage** | Operations (abstract first, then physical maps), counter-intel, exposure |
| **M7: War** | War goals, war score, warbands and interception, occupation, cold war, joint wars |
| **M8: Broadcast and culture** | Broadcast Center, programs, TV and newspaper hooks, ideo influence |
| **M9: Integrations** | One integration per sub-milestone (the core bindings for E&D and FTV land in M1, since both are required): FTV (full) → E&D (full) → Simple Leadership → VOE → Visit Settlements → RimMarket → RimToll → RimCourt → IRBM → CBF → VBE → P&P |
| **M10: Ending and polish** | Unification ending, events catalogue, balancing, translation keys |

Integrations are designed into the event bus from M0, so M9 is wiring and not restructuring. Any integration can be pulled forward.

---

## 9a. M1 implementation notes

How the world simulation works as built (tunable values in brackets are code constants or settings):

| System (order) | Runs | Does |
|---|---|---|
| Economy (0) | Daily per faction | Reads adults, total population and stockpile wealth from E&D. Economy index = wealth per head ÷ world average × 100, smoothed. |
| Territory (1) | Every slot + daily | Scans FTV ownership of all surface tiles in 240 pieces across the day; counts FTV vassal outposts per master and per (original, master) pair. |
| Capital (2) | Daily per faction | CBF seat if present, else the oldest settlement. Raises *capital lost* if destroyed or captured, *new capital* when it moves. |
| Military (3) | Daily per faction | Strength = (settlements·8 + adults·0.5 + vassal outposts·4 + territory·0.01) × tech factor × √(economy/100) × (1 − weariness/200). |
| Relations (10) | Daily per faction | Opinion of every other faction and the player moves 10%/day toward a target from ideology compatibility (±30), tech gap, paranoia, border friction (settlements within 25 tiles), shared enemies, grievances (decay 2%/day), vassal outposts the other holds, the current relation kind, and player goodwill. |
| Politics (20) | Daily per faction | Weariness, legitimacy, stability drift toward a target; blocs drift; riots (stability < 30), coups (stability < 20 and legitimacy < 40), revolutions (10+ days below 10). |
| AI diplomacy (30) | Daily per faction | Goodwill between AI factions follows their opinions without crossing kind thresholds. With an 8–15 day cooldown, an AI may make peace, ally, declare war (needs hatred, aggression, relative strength, proximity, stability; max 2 wars; respects 30-day truces) or break an alliance. Relations with the player are never changed here. |

Relation changes go through vanilla base goodwill and `SetRelationDirect` on both sides, so vanilla and other mods see the same state. The Harmony hook on `Notify_RelationKindChanged` turns every change into a Dominion event and names the aggressor when Dominion caused it.

## 10. Decisions log

| # | Question | Decision |
|---|---|---|
| 1 | Package ID | `ketschbopp.dominion` |
| 2 | The News | Dominion is marked `incompatibleWith` it |
| 3 | Rim Council membership | Tribals excluded; no one has a veto (dev-mode player only) |
| 4 | Physical vs abstract operations | The player chooses per operation |
| 5 | Splitting factions in civil war | No. Revolutions change the regime inside the faction |
| 6 | Foreign influence | Mainly politics; ideology to a lesser degree (minor, rate-limited reforms) |
| 7 | Head of state and diplomats | Setting: ideology role (leader / Diplomat role) or chosen at the Leadership Desk |
| 8 | Art | Made in-repo by script |
| 9 | Integration order | As in M9 |
| 10 | Economics & Demography | **Hard requirement** (added later by the author) |
| 11 | Faction Territories & Vassalage | **Hard requirement** (added later by the author) |
