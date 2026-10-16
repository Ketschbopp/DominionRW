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
| **Visit Settlements** (`NinaGoblin.VisitSettlements`, Nina.Goblin's build; the original `alt4s.VisitSettlements` also activates it) | Settlement maps the player is visiting | Physical espionage and special operations happen on the real visited map; theft or destruction there raises Dominion incidents; summits can be held in a visited settlement | `WorldComponent_SettlementData.settlementMaps` (tile id → visited map), its penalty history events `ItemStolenFromSettlement` / `VandalizedInSettlement` / `EncroachedInSettlement` / `ContainerOpenedInSettlement` (listen) |
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
| **M2: News** ✅ | News feed tab, visibility and intel, grammar headlines, radio, research tree, colony thoughts |
| **M3: Diplomacy UI** ✅ | Dominion tab, dossiers, actions, treaties, embargo and border rights, Leadership Desk |
| **M4: Physical diplomacy** ✅ | Envoys, delegations, summits plus the dialogue system, hostage exchange |
| **M5: Rim Council** ✅ | Sessions, motions, voting, decrees, violations |
| **M6: Intelligence** ✅ | Operations (abstract first, then physical maps), counter-intel, exposure |
| **M7: War** ✅ | War goals, war score, warbands and interception, occupation, cold war, joint wars |
| **M8: Broadcast and culture** ✅ | Broadcast Center, programs, TV and newspaper hooks, ideo influence |
| **M9: Integrations** ✅ | One integration per sub-milestone (the core bindings for E&D and FTV land in M1, since both are required): FTV (full) → E&D (full) → Simple Leadership → VOE → Visit Settlements → RimMarket → RimToll → RimCourt → IRBM → CBF → VBE → P&P |
| **M10: Ending and polish** ✅ | Unification ending, events catalogue, balancing, translation keys |

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

## 9b. M2 implementation notes

**Intel** (player → each faction, 0–100) moves 10% per day toward a target: 5 base, +8/+8/+12/+10 for the four intel research projects, up to +25 from radio coverage, +25 if allied (+3 to +8 if neutral), +15/+8 if the faction is within 15/40 tiles, minus up to 10 for a paranoid faction and 5 for juntas and autocracies. Espionage (M6), IRBM satellites, VOE outposts and Visit Settlements (M9) add to it.

**Visibility.** Secrecy needs intel: Public 0, Restricted 25, Secret 50, BlackOps 80 (the best intel on either faction involved counts). Events involving the player are always known at once.

**Delivery.**

| Route | When | Delay |
|---|---|---|
| Radio intercept | Public event, radio researched, coverage > 10 | 1–6 h |
| Diplomatic dispatch | An ally of the player is involved | 6–24 h |
| Intelligence report | Restricted or higher | 12–48 h |
| Traveler gossip | Everything else | 1–4 days |

A report is *Confirmed* if it came by radio or dispatch or intel is 30+ above what was needed, otherwise a *Rumor*. Events the player cannot see stay hidden for up to 30 days and surface if intel grows, or leak (4% / 2% / 0.5% per day for Restricted / Secret / BlackOps).

**False rumors.** Each day, each faction the player has under 40 intel on adds a 2% chance of a baseless rumor (war, riots, coup, leader's death). Once intel on that faction reaches 50 a correction is published, and the original report is marked as false.

**Radio.** Radio coverage (0–100) rises while a colonist monitors a powered receiver (Research work; about 4 per hour, scaled by Intellectual skill and hearing) and decays 30% per day. Colonists stop monitoring at 90.

**Text.** Headlines and stories come from vanilla grammar (`RulePackDef` per event, plus the shared `Dominion_NewsCommon`) and are resolved once at delivery, then saved.

**Dossier gating.** Leader, capital and government are always known; stability, population, economy and military need 25; wealth, war weariness, personality and blocs need 50; a faction's opinions of others need 80. The raw event log is dev-mode only, since it shows everything.

## 9c. M3 implementation notes

**Influence** (0–100, start 20) is the player's political capital. The head of state earns it at the Leadership Desk (statecraft work, 1.5 per hour × (0.5 + Social/10), ×1.25 at a steel or gold desk) and spends it on actions.

**Head of state** comes from the setting: the ideology leader (vanilla `IdeoRole_Leader`), or a colonist chosen at the desk (also the fallback without Ideology). Their Social skill adds (Social − 8) × 1.5% to every acceptance chance; a gold desk adds 8%. *Diplomats* (envoys sent in their place) arrive with physical diplomacy in M4.

**Actions** (`DiplomacyActionDef`, data-driven with a worker class):

| Tier | Needs | Actions (influence, cooldown) |
|---|---|---|
| Basic | Comms console or desk | Gift (silver, 3 d), Compliment (5, 10 d), Insult (0, 5 d), Propose peace (20, 7 d), Declare war (0) |
| Statecraft | Desk and head of state | Propose treaty (5 + treaty, 5 d; Diplomatic Corps), Border policy (5, 5 d), Embargo (10, 10 d), Denounce (15, 30 d), Demand tribute (25, 30 d) |

**Treaties** (`TreatyDef`, data-driven): alliance (vanilla ally + defensive), non-aggression pact (120 d), trade agreement (180 d, ±10% prices, +3% economy), defensive pact, intelligence sharing (120 d, +20 intel on the partner and +8 on its enemies, secret). AI acceptance = base + opinion/200 + personality weights + leadership bonus, zero below the treaty's minimum opinion. Declaring war breaks every treaty between the pair; breaking a treaty gives the victim +40 grievance and lowers every faction's opinion of the breaker (more for honourable factions). Defensive partners are called to war an hour later: an AI joins with chance 0.2 + 0.7 × honour (halved if it has its own pact with the aggressor), otherwise breaks the treaty; the player gets a choice. AI factions sign trade agreements, non-aggression pacts and defensive pacts with each other, and honourable ones rarely attack treaty partners.

**Borders** (per side, start open): restricted turns away visitors, closed turns away visitors and traders and blocks trading at that faction's settlements. **Embargoes** stop the target's trader caravans and settlement trade, cost the target 4% economy each, and an AI embargo on the player adds a 40% markup (for traders who still come, such as orbital ones). AI factions close borders below −30/−50 opinion, embargo below −60, and lift both as relations recover. Liked players get treaty offers (at most every 30 days per faction).

**Opinion inputs added:** treaty opinion bonuses, *favour* (gestures; decays 2%/day), −15 if the other side embargoes you, −8/−3 for its closed or restricted border.

**Prices** are combined in one Harmony postfix on `Tradeable.GetPriceFor`, cached per faction per tick, multiplied on top of other mods' prices.

## 9d. M4 implementation notes

**Envoys.** The head of state can always act as an envoy. Diplomats come from the setting: colonists picked at the Leadership Desk, or (with Ideology) holders of any ideology role other than leader. *A dedicated "Diplomat" role precept is deferred until the mod can be tested in game: a role def needs fields (name generator, icon, requirements) that cannot be verified without the game, and a wrong one breaks loading.*

**Envoy missions.** A caravan carrying an envoy gets options on every foreign settlement (a `WorldObjectComp` added to `Settlement` by XML patch). The caravan travels there and the mission runs on arrival:

| Mission | Influence | Effect |
|---|---|---|
| Improve relations | 10 | Favour +10 + 0.8 × Social, goodwill +3 + Social/4 |
| Negotiate a treaty | treaty cost | Treaty acceptance + in-person bonus (+15%, ±1.5% per Social above/below 8) |
| Peace talks (hostile) | 20 | Peace acceptance + in-person bonus; on failure, 35% × aggression chance the envoy is taken captive |
| Gather intelligence | 10 | Intel +10 + Social |
| Exchange hostages | 5 | See below |
| Attend summit | — | Opens the summit (head of state only) |

Each mission has a 10-day cooldown per faction.

**Hostages** are real pawns: your colonists in the faction's vanilla kidnapped list, and their people held as prisoners in your colonies. An exchange releases your prisoners (they walk off) and returns your colonists (they walk in at the capital, or arrive by drop pod). Acceptance: 25% + 20% × (your leverage − 0.7 × their captives) + opinion/300; holding their leader counts as 3 extra prisoners. Freeing prisoners without asking anything back always works and earns favour and goodwill. Available over the comms, as an envoy mission, or as a summit agenda.

**Delegations** are world objects that follow a real world path at caravan speed (2,000 ticks per tile). They are drawn only if they involve the player or the player has intel 25 or more on either faction. AI-to-AI peace, alliances and treaties now travel by delegation and take effect on arrival, so they can be stopped. AI treaty offers to the player arrive as an envoy who walks into your capital with guards. A player caravan on the same tile can **intercept** a delegation: this is an act of war, the escort (combat points 200 + strength/2, capped at 1,200) and the envoy, or the leader in person if travelling to a summit, fight on a real caravan battle map, and the mission fails.

**Summits** (statecraft action, 30 influence, 30-day cooldown) have an agenda (better relations, peace, a treaty or a hostage exchange) and a venue:
- **Your capital:** their leader really travels there as a delegation and walks in with three guards. The talks open as soon as your head of state is on the same map, and the guests wait about a day.
- **Their capital:** you have 25 days to bring your head of state there by caravan and attend.

Attendance chance is 50% + opinion/150 + leadership bonus; hostile factions come only for peace (25% + weariness/120 + 0.4 × doves).

The **dialogue** runs three rounds. Each round offers up to four approaches drawn from: common ground, flattery, concession (300 silver or 10 influence), threat, apology, leverage (prisoners), shared enemies, shared beliefs, and plain speaking. Each has a success chance and a rapport effect set by the head of state's Social skill and the other leader's personality: the honourable resent threats and blackmail, the greedy like concessions, and the paranoid distrust flattery. There is a 15% chance per round of something happening at the table. Final chance = agenda base + rapport/150 + in-person bonus. Walking out costs favour and adds grievance.

## 9e. M5 implementation notes

**Membership:** every simulated humanlike faction at or above the tech threshold (default Medieval, so tribals are out), except outlaws (permanent enemies). The player joins when above the threshold or after researching Diplomatic Corps. *Tribal vassals sitting in through their overlord waits for faction-level vassalage (M7/M9).*

**Cycle:**
- A session every *council interval* days (default 60, one year).
- Fifteen days before, it is announced. The host rotates by load id among members with a capital; the player can host only with a Leadership Desk.
- AI members put up to six motions on the agenda, and every AI member sends a real delegation to the host capital.
- Only members present vote. A delegation that is intercepted, or too far to arrive in time, means that member is absent.

**The player attends** by registering an envoy at the host capital (caravan mission "Attend the Rim Council"). When the player hosts, delegates walk into the capital and stay, and the head of state or a diplomat must be on that map when the session opens.

**Voting:**
- Vote weight = 1 + population/100 + economy/200 + military/300 + 0.25 per allied member present. The player's weight is 1 + colonists/10 + strength/300.
- AI leaning = the motion's own utility + opinion of the sponsor/200 (+0.3 if allied, −0.4 if hostile) + lobbying. Above 0.1 votes yes, below −0.1 votes no.
- A motion passes if yes outweighs no.
- The player can lobby each member once per motion: 10 influence, success chance 40% + 3% per Social, moving that member's leaning by 0.35 toward the player's vote.
- No vetoes; a dev-mode veto exists for testing.

**Motions** (`CouncilMotionDef`, data plus a worker):

| Motion | Decree |
|---|---|
| Sanctions on X | Members must embargo X within 5 days, for 60 days; X's economy −5% |
| Censure of X | Every member's opinion of X −15; X loses legitimacy and stability |
| Ceasefire X–Y | Peace within 7 days, kept for 60; restarting the war is a violation |
| Collective defense against X | Members must be at war with X within 10 days |
| Trade standard | 120 days: 5% better prices between members, +2% economy |
| Diplomatic immunity | 180 days: intercepting delegations or seizing envoys is a violation |
| Ban on slavery | 180 days: the player must free their slaves within 10 days |

AI members comply by honour (and war weariness or hawkishness, as relevant); the player gets letters and dialogs.

**Violations:** every other member's opinion of the violator falls (6 + 8 × honour), the player loses 5 goodwill with each member, and an AI violator loses legitimacy. Violations make sanctions and censure motions against the violator more likely.

## 9f. M6 implementation notes (intelligence and covert operations)

M6 was first built as intelligence gathering; the covert operations of §5.5 were added with M7 (see the end of this section).

- **Research:** *Intelligence networks* (Medieval, after Diplomatic Corps) unlocks the Intelligence tab.
- **Operations** (`OperationDef`, data plus a worker). Up to three agents leave the colony for the duration, as world pawns, and walk back in at the capital:

| Operation | Days | Skill | Cost | On success |
|---|---|---|---|---|
| Establish a network | 6 | Social | 10 influence | Network +25 |
| Gather intelligence | 3 | Intellectual | 8 influence | Intel +20 (+ network/10); pending reports about them arrive at once |
| Spread propaganda | 4 | Social | 12 influence | Their opinion of you +10, doves +5% |
| Bribe an official | 2 | Social | 5 influence + 400 silver | Opinion +15, intel +10 |

- **Success chance:** base + 3% per skill level above 5 + network/200 + 5% per extra agent − 15% × target paranoia, +5% with Intelligence Agency, −5% against juntas and autocracies.
- **Exposure:** a failure has its own exposure chance (less 10% with a network over 50). Exposure costs favour and adds grievance, and there is a 40% chance one agent is detained. Detained agents go to the faction's kidnapped list and can come back through a hostage exchange.
- **Networks** (0–100) add network × 0.3 to the intel target. They fade 0.3 per day, and a paranoid faction may uncover half of one (paranoia × 1% per day).
- **Counter-intelligence:** a 15-influence sweep raises the catch chance by 30% for 30 days.
- **AI services:**
  - AI factions run operations on the player (1% + aggression × 1% per day, +1% if they dislike you). Caught at 20% + sweep + 10% with Intelligence Agency + intel/300; when caught, every faction's opinion of the culprit falls. Uncaught propaganda gives colonists a small mood debuff.
  - AI factions also gather intelligence on their rivals. When agents are caught, it is reported as a *secret* event, which the player only hears about with intel 50 or more.

**Covert operations.** `OperationDef` says whether an operation runs off-map (`allowAbstract`), in person (`allowPhysical`) or both, what the agents must do on the map (`objective`), whether it needs a faction to blame, a minimum network, a maximum target stability, the event it raises on success, and whether it is a hostile act. Doing it in person multiplies the reward by 1.5.

| Operation | Mode | Cost | On success |
|---|---|---|---|
| Gather intelligence | both (in person: reach their archives) | 8 influence | As before; in person ×1.5 |
| Incite riots | off-map, 5 days | 15 influence + 300 silver | Stability −10, reformists +5%, riots; a shaky regime (stability < 20, legitimacy < 45) may fall to a coup (30%) |
| Stage a coup | off-map, 10 days; network 30+, stability ≤ 45 | 40 influence + 1000 silver | Coup, and the new regime signs a **puppet** treaty with you: favour and opinion at least 30 |
| False flag | off-map, 6 days; choose a faction to blame | 25 influence | Target's grievance against the blamed +35, opinion −20; the world hears that the blamed faction's agents were caught; an aggressive target may declare war on them |
| Steal technology | both (in person: reach a workshop) | 15 influence | 30% of your current project (more against more advanced factions, ×1.5 in person). Needs a faction at least as advanced as you |
| Assassinate the leader | in person only | 25 influence | The leader dies (or, if away, a senior official: stability and legitimacy −8) |
| Sabotage | in person only | 15 influence | Economy −12%, military −15% for 30 days, 2,250 silver of E&D stockpile destroyed, war score +7.5 |
| Rescue a captive | in person only | 5 influence | A colonist they hold is freed |

- **In person.** A caravan carrying the agents (every able colonist in it) picks *Covert operation* at one of the target's settlements. On arrival the settlement map opens and the caravan walks in. The target is marked: the leader (brought in from the world if free; otherwise the toughest humanlike there), a building (power and batteries first for sabotage, the most valuable one for theft), or the captive, spawned as their prisoner. Killing or destroying the target, or standing next to it, completes the objective. Unless the faction is already at war with you, its guards are not hostile; every hour each agent on the map has a 1.5% chance (× 0.5 + paranoia, ×2 once the deed is done, ×0.7 with Intelligence Agency, × the detection setting) of being noticed, and then the faction declares war. The operation ends when no agent is left on the map: success if the objective was reached, exposed if the alarm was raised (or by the def's chance after success). A captive not rescued goes back to their cell. One in-person operation at a time.
- **Exposure** of a hostile act costs twice the favour and grievance, and the target may declare war (15% + 30% × aggression). Off-map, an agent may still be detained (40%). A false flag that comes out also angers the faction that was blamed.
- **AI services** that get past the rival's defences sometimes act instead of only gathering: incite riots (stability −6), sabotage (economy −4%, military weakened 10 days, war score), false flags against the rival's other enemies, coups against shaky rivals and assassinations, more often between factions at war or in a cold war. All are secret or black-ops events. Spies interested in you are more active in a cold war or a war, and may sabotage a building in your colony (it breaks down).

## 9g. M8 implementation notes

**Broadcast Center** (research *Broadcasting*, after Radio monitoring): a 2×2 powered studio. Pick a programme (and an audience faction where needed); a colonist on research work runs it in two-hour shifts. Airtime = hours × (0.5 + Social/10), capped at 8 per programme per day. Airtime turns into effects once a day.

| Programme | Daily effect | On TV |
|---|---|---|
| News bulletins | Radio coverage + 1.5 × airtime; colonists *well informed* (+2) with 2 or more hours | +1 |
| Cultural programmes | Cultural influence on factions within 60 tiles (0.5 × airtime, scaled by distance), small favour | +1 |
| For a foreign audience | One faction: favour + 0.5 × airtime (max 5), cultural influence + airtime | +1 |
| Counter-programming | Blocks foreign propaganda mood effects for 1.5 days | +1 |
| Entertainment | None | +3 |

**TV:** colonists watching any vanilla television get the on-air programme's memory once an hour. A programme counts as on air for 6 hours after its last airtime.

**Newspapers** (Vanilla Books Expanded): newly printed newspapers carry the latest Dominion headline as their title and the three most recent headlines (plus the current programme) as their text.

**Cultural influence** (per faction, 0–100) fades 1% per day and grows with treaties (+0.15 per day, alliances +0.3). It adds influence × 0.15 to the faction's opinion of the player, and × 0.05 more as ideological affinity while *ideology reforms* are allowed. *Actual precept changes in other factions' ideologies are not implemented yet: editing an ideology's precepts needs in-game testing first.*

## 9h. M9 implementation notes

Every integration is one class, found by reflection and bound by name at startup. A binding failure switches only that integration off and is listed in the self-test (dev menu). Integrations feed the core through modifier hooks that the systems add up daily: opinion, intel, stability, economy and council vote weight, plus a daily tick and every event on the bus.

| Mod | What it does in Dominion |
|---|---|
| **Faction Territories & Vassalage** (required) | Territory and vassal outposts counted daily. New envoy mission *Negotiate vassalage*: 30 influence, chance from the faction's opinion of you and your relative strength; on success FTV turns the settlement into your vassal outpost (FTV's own goodwill cost applies). Gaining or losing vassal outposts is news. Council weight: +1 per 250 territory tiles (max +2), +0.2 per vassal outpost (max +2) |
| **Economics & Demography** (required) | Population and wealth source. Each embargo against a faction removes 0.2% of its stockpile a day (max 1%, setting). Riots move 1% and revolutions 3% of the adult population (max 40) to the most stable friendly faction (setting); this is news |
| Simple Leadership | Capital governor in the dossier. A new power event in a faction: stability −4, legitimacy −5, *power struggle* news |
| Vanilla Outposts Expanded | Your outposts add +0.3 council weight each (max +3); +8 intel on factions with a settlement within 15 tiles of one |
| Visit Settlements (`NinaGoblin.VisitSettlements`, or the original `alt4s.VisitSettlements`) | Visits are read from its registry of visited maps (`WorldComponent_SettlementData.settlementMaps`), so attack and operation maps do not count. Each day of a visit to a non-hostile settlement: +2 intel, +0.5 favour and +0.5 cultural influence. Its goodwill penalties (theft, vandalism, encroaching, opening containers) also add grievance (half the goodwill lost) and cost favour (0.3×), and are news (*incident during a visit*, at most once a day per faction). Colonists already visiting a settlement can run an in-person covert operation there from the Intelligence tab, and a caravan sent on one joins the visit instead of being refused. A summit arranged at their capital opens as soon as your head of state is visiting it. |
| RimMarket | Market reputation sets opinion of you (±10) |
| RimToll | Toll resentment lowers opinion of you (up to −20) |
| RimCourt | Your judge reputation sets opinion of you (±8) |
| IRBM | Satellite coverage of a faction's home tile: +15 intel. While the doomsday clock is running every faction loses 10 target stability |
| Custom Base Framework | Seats count as capitals (since M1) |
| Vanilla Books Expanded | Newspapers print Dominion headlines (M8) |
| Pillage & Plunder | A newly plundered settlement: stability −3 and news; −5% economy per settlement still recovering |

Change counters (power events, plundered settlements, vassal outposts) are not saved: the first day after loading only records the current state, so loading never produces stale news.

*IRBM council motions and tribal vassals in the council are still deferred.*

## 9i. M10 implementation notes

**The Federation.** Research *Planetary Federation* (4000, after Diplomatic Corps and World Comms Protocols), then propose the **Federation Charter** at the Rim Council (40 influence; only the player can propose it; it never expires). Members lean yes with their opinion of you and honour, and no with paranoia and aggression. With the council switched off, the research alone puts the charter in force. Then any major faction can sign **Federation membership** with you by comms, envoy or summit (60 influence, opinion at least 50; acceptance adds a bandwagon bonus of up to +30% for the share already unified, your cultural influence and your relative strength). Members get non-aggression, a defensive pact, +10% trade, +4% economy, +25 intel and +20 opinion. Friends above 70 opinion offer to join on their own. A member whose opinion of you drops below −15 may leave.

**Unification.** Major factions are visible, humanlike, with a settlement (outlaws only with the setting). One is unified when it is a **member**, **subjugated** (you hold at least as many of its former settlements as FTV vassal outposts as it has left), or **conquered** (fell while at war with you). A member destroyed by someone else is lost, not unified. The Federation tab shows the steps, a progress bar and every faction's status. When the required share (setting, 50–100%) is unified you are offered the proclamation; the credits text depends on the path that brought in more than half the factions (or a mixed ending). The game continues afterwards.

**World events** are `DominionEventDef`s with a `<world>` block (conditions, partner choice, effects), so new ones are XML-only. Each faction rolls once a day; at most one world event per faction every 8 days and the same one not within 60 days. Effects apply whenever the event is raised, including from the dev menu.

| Event | When | Effect |
|---|---|---|
| Political scandal | any | legitimacy −8, stability −3 |
| Succession crisis | legitimacy ≤ 40 | stability −8, legitimacy −5 |
| Official defects | has a rival | legitimacy −4, opinion of the rival −10 |
| Reform movement | stability ≤ 60 | Reformists +6%, stability −2 |
| Secession movement | stability ≤ 30, 2+ settlements | stability −6, legitimacy −4 (factions never split) |
| Religious schism | Ideology | Zealots +5%, stability −5 |
| Cultural festival | stability ≥ 50 | stability +4, legitimacy +2 |
| Famine | economy ≤ 70 | −2% population, stability −6, economy −5%, crisis 20 days |
| Plague outbreak | any | −1.5% population, economy −6%, stability −4, crisis 20 days, 25% spread to factions within 25 tiles |
| Natural disaster | any (at a settlement) | economy −7%, stability −3, crisis 15 days |
| Refugee wave | stability ≤ 30 | 2% of the population moves to the most stable neighbour |
| Trade boom | economy ≥ 90, at peace | economy +8%, stability +3, warmer ties with a trade partner |
| Good harvest | any | economy +4%, stability +2 |
| Market crash | economy ≥ 120 | economy −10%, stability −4 |

**Aid.** A faction in crisis can be helped: friendly AI factions with a healthy economy may send aid (news, favour, stability), and the player can take a caravan with an envoy to one of its settlements and **deliver aid**: medicine, then food, then silver, at least 300 and up to 1500 silver worth. Favour up to +25, goodwill +2 to +12, cultural influence +5, their stability +4, and the crisis ends.

*Assassination attempts* were added with M7 (legitimacy ≤ 60, mtb 900 days: stability −4, legitimacy −3, a 30% chance the leader dies; `leaderDeathChance` in the `<world>` block). Border skirmishes and arms races come from cold wars (§9j). *Visitable world sites are handled through the faction's own settlements (aid deliveries) rather than separate site objects.*

## 9j. M7 implementation notes (war)

**War records.** Every declaration (vanilla, AI, player, a joined war) opens a `War`: attacker, defender, goal, war score (−100 defender won … +100 attacker won), battles, and the ally it was joined for, if any. Permanent enemies and quest factions never get one. Old saves adopt every war Dominion knew was active.

**Goals.** The player picks one when declaring war; the AI weighs them by personality and situation:

| Goal | AI weight | If the attacker wins |
|---|---|---|
| Punitive | 0.4 + grievance/100 | Loser: stability −12, legitimacy −10, economy −8% (you: −20 influence) |
| Conquest | 0.2 + aggression + 0.4 if within 30 tiles + 0.3 × greed; not against you | The target settlement (the defender's nearest, not the capital) is ceded. To you it becomes an FTV vassal outpost; if FTV cannot do it, they pay instead |
| Vassalisation | stronger by 1.4× or more; not against you | A **puppet** treaty. Against you: you pay tribute |
| Tribute | 0.2 + greed | A payment now and a **tribute** treaty for 180 days |
| Regime change | ideologies clash, or the defender is unstable | A coup in the loser, and the new regime likes the winner. Against you: −30 influence |
| Liberation | the defender holds settlements taken from the attacker | Every settlement the loser took from others goes back |

A winning defender, or a faction that joined for an ally, takes reparations. A payment is 2% of the payer's wealth (300–3000; from you 2% of home wealth, 200–2000), drop-podded to you or moved between economies.

**Score** comes from battles (+10 for a win, +8 for holding), occupations (+15, and a lost capital +20 more), the defeat of a settlement by you (+25), deaths in combat (0.6 per humanlike, 10 for a leader, at most 12 a day), intercepting a warband (+6), blockades (+1.5 a day), sabotage, proxy funding (+10) and reinforcements (+6). It also drifts each day toward the stronger side (up to 0.6) and against the wearier one. What one side gains in a joined war, half goes to the ally's own war.

**Peace.** When peace is made (AI delegation, your proposal, a summit, a ceasefire decree), the side ahead by 25 or more sets the terms; below that it is a white peace and occupied settlements go back. Occupied settlements stay with the winner after a capitulation, a conquest, or a lead of 50. War score now counts in peace acceptance (yours: −30% to +40%) and in the AI's own peace chances. When a war ends, wars joined for either side wind down too (you are offered peace).

**Capitulation.** At the capitulation score (setting, default 75) the AI loser gives up (25% a day, more when weary or dovish) on the winner's terms, with a 60-day truce. With you on either side you are asked, at most every 15 days.

**Warbands** (`Warband` world object, 2,500 ticks a tile, at most 2 per faction). Each day of a war each AI side may send one: chance (5% + 12% × aggression) × frequency × (1 − weariness/150). Strength is 5 × military strength × (1 − weariness/200), 250–3000 points. Missions:
- **Siege** (the conquest target, the settlements to liberate, or one of ours they hold, else the nearest): an abstract battle, attack ÷ (attack + 1.2 × garrison), garrison = 5 × strength ÷ settlements (+50% at the capital). Both lose population through E&D and gain weariness. A win occupies the settlement (`Settlement.SetFaction`; never the player's, never a faction's last one, never a settlement with an open map).
- **Blockade** (15%): six days at the enemy capital, economy −1% and war score +1.5 a day.
- **Raid** on the player's capital (with the setting, half as likely, at most one at a time, 12 days apart): a vanilla raid sized by the storyteller.
- **Reinforce:** an ally at peace marches to its friend's capital (+6 score in its wars).

Warbands are drawn when they involve you, when you have intel 25 or more on their faction, or within 8 tiles of your colonies or caravans. One marching on you sends a letter a day before it arrives. A caravan on the same tile can intercept it: a caravan battle against its full strength, and an act of war if you were at peace.

**Blockades by the player:** a caravan with a fighter at an enemy settlement can blockade it: economy −1%, 150 silver of stockpile and +1.5 war score a day, while it stays. Each day the garrison may sally out (10%).

**Joint wars.** Two days into a war each AI side asks its allies and puppets, once each. AI allies join with 15% + 35% × honour (+20% if they dislike the enemy, −15% per war they already fight, ×0.3 with a pact with the enemy; puppets 90%); a refusal costs favour and is news. You are asked by dialog. You can ask others with **Call to arms** (15 influence). Defensive pacts now join through the same path.

**Puppets and tribute.** `Dominion_Puppet` (imposed: puppet pays 2% economy, never attacks, joins wars, counts as subjugated for the Federation, +15 intel) and `Dominion_Tribute` (180 days, 4% economy; with you, silver every 15 days: 0.3% of their wealth (100–600), or from you 0.5% of home wealth (150–1000), and not paying breaks the treaty). A puppet that dislikes its overlord (opinion < −25) and is stable (45+) may break free (2% a day); an AI overlord may answer with war.

**Cold wars.** Two AI peers (strength ratio 0.5–2) whose opinions of each other are both below −45 enter one (2% a day); it thaws when both rise above −20 (10% a day) and ends if it turns hot. Each side: an arms race (military +8% per cold war, up to +20%; economy −2% each), hawks grow, propaganda battles, border skirmishes between neighbours (grievance both ways, news), and proxy wars (funding whoever fights the rival: +6 score to them). A cold war makes declaring war 1.5× more likely. You can declare one (10 influence) or offer to end it, and fund a **proxy war** (500 silver, 15 influence, +10 score to your proxy; leaks one time in four).

**Interfaces:** a War tab (wars with goal, score bar and the terms a peace would bring now, peace and call-to-arms buttons; cold wars; warbands in sight; occupied settlements; blockades), a goal menu when declaring war, settings (War and Espionage tabs) and dev actions.

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

---

## 11. Status after M10 and the review pass

**Nothing has been tested in game yet** (M7 and the covert operations included). Everything builds against the 1.7 reference assemblies and every def, key and texture is checked by script, but no save has been loaded. The first in-game session is the most important next step.

### Left out on purpose

- *(M7 war and the covert operations of §5.5 were added after this review; see §9f and §9j.)* Still left out: naval blockades, VOE outposts intercepting warbands, IRBM missile strikes as war acts, RimCourt trials for captured operatives, and physical coups.
- **Hostile broadcasting**: the propaganda program aimed at a faction's stability and the disinformation program.

### Deferred, still to do

| Area | Missing |
|---|---|
| Diplomacy actions | threaten, apologise, military access, research cooperation, political marriage (RimCourt), guarantee of independence, loans, investment, release vassal, annexation |
| Vassalage | puppets exist (war goal, coups); releasing or annexing them; tribal vassals sitting in the council through their overlord |
| Rim Council | non-proliferation (IRBM), plague quarantine, recognition of territory, peacekeeping coalition and admit-member motions |
| Culture | real precept changes in other factions' ideologies (reforms only move opinion today); a dedicated Diplomat role precept; precepts gating actions (e.g. pacifists declaring war) |
| Summits | in-summit incidents beyond the current ones (protest, betrayal); hostage exchanges at an agreed time and place (today they happen at once) |
| Economy | trade-screen tooltip explaining each price factor; traders from plague-hit factions bringing disease |
| Map and UI | Overview world-map overlay (territory, alliances, wars); the paper-document look with leader photos and stamps (windows use the standard RimWorld style) |
| World events | visitable world sites (rallies, ceremonies, disaster relief on site); aid is delivered at the faction's own settlements instead |
| Other | translations beyond English; balancing from real play |

### Review pass (bugs and efficiency)

A four-way code review found no crash on a normal path; it found about 60 smaller problems, and these were fixed:

- **Lost or duplicated pawns:** a faction leader removed from the world when a summit could not take place; kidnapped colonists vanishing in a hostage exchange with no home map; a leader pulled out of a caravan or a prison into a summit; an envoy's luggage lost on capture, and a caravan left empty when its only colonist was taken; agents stranded when an operation's target vanished or there was no colony map (they now wait and come home later).
- **Stale actions:** envoy missions now recheck everything on arrival (war declared, treaty already signed, research missing, summit called off); intercepting a delegation that already moved on; offers that can no longer be signed; summits with a faction that is gone; a summit "arranged" letter after it had already been called off.
- **Rim Council:** a session could get stuck forever when a motion's target faction was removed; one broken decree stopped every later decree; sanctions lifted embargoes that existed before them; switching the council off froze decrees with their effects; a delayed announcement gave delegates an hour instead of 15 days; one-time motions (censure) lingered as 30-day decrees.
- **Unification:** factions finished off by others no longer count as yours; subjugation needs at least half of what a faction once had, counted fresh from FTV; conquest needs a settlement you beat in battle.
- **Across saves:** systems kept data from the previous save (territory scan, world wealth, outposts, pending calls to war); capitals destroyed just before a save were never reported lost; personalities were zero until a faction's first daily update; quest factions left dangling references in the event log and news.
- **Balance:** a treaty signed at a summit now costs half its normal influence on top of the summit; tribute is not reported as paid when there is no colony to receive it; a concession can no longer be made for free.
- **Efficiency:** shared enemies were O(F³) per faction per day (now computed once per pair per day); settlement distances were recomputed daily for every pair (now only after settlements change); the Diplomacy, Council, Federation, dossier, intelligence and summit windows recomputed silver, hostages, vote weights and chances every frame (now cached for half a second of real time); broadcasting built a new string key every few ticks; every event copied the decree list.
- **UI:** the dossier profile was cut off (it now scrolls, and the dossier is taller); scroll positions reset when switching factions or settings tabs; long names and reasons are truncated with tooltips; Statecraft no longer needs the Intellectual work tag (a head of state who cannot do intellectual work could not earn influence).

### Second review pass

- **Engine use:** faction leaders now really travel to summits (they were never treated as free to go); colonists rescued into a caravan stay saved; released prisoners walk off the map; operations cannot send your last colonists away (that ended the game) and agents leave the map properly; delegations never path across Odyssey layers; hostile factions no longer arrive as summit visitors; buy-and-resell arbitrage under stacked trade bonuses is capped; Statecraft is switched on once for colonists in existing saves, and the desk says when the head of state cannot govern; spare generated guards are discarded.
- **Regressions from the first pass:** player-initiated war and peace effects on the other side, sanctions embargoes from older saves, conquest credit with FTV's settlement-defeat patch, operation agents offered twice, cached silver or chances used for real spending, stale settlement distances, the summit treaty share charged when no summit happened.
- **Balance over a long game:** favour is capped at ±40 and broadcasts give less of it (opinion could be pinned at 100, making the Federation trivial); AI Federation offers need opinion above 80; war weariness tapers near the top and war-weary enemies now sue for peace; Rim Council motions need yes from a third of the room's weight, not just the sponsor; Collective Defence accepts an embargo, excuses allies, pact partners and members in two wars, and is joined far less eagerly; old violations are forgiven over time; AI alliances and non-aggression pacts can actually form; the dead ally-to-defensive-pact branch is gone; the embargo drain is capped at 0.4% a day; the economy no longer drifts towards average after loading; influence income is lower (0.6 an hour base) so it is a real budget; false rumours are capped; the Charter is in force without a council when the council cannot meet.
- **Text:** treaty names get the right article; news no longer prints "its leadership" or "an unknown party" as people and places; pass/fail and presence markers are words instead of glyphs; British spelling throughout; operation costs, lobbying, empty filters and several letters read correctly; About.xml now describes what is actually in the mod.
- **Known gaps from this pass:** economy shocks from world events still fade within about a week (tuning); the ideology-reform setting only affects opinion until precept changes are built; the newspaper hook may need VBE's own override (untestable without the game).

