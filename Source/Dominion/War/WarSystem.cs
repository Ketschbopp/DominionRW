using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Dominion
{
    /// <summary>
    /// Runs the wars: opens a war record on every declaration, settles the peace terms when it ends, lets the war score
    /// drift with the balance of strength, sends AI warbands, calls allies in, forces capitulations, collects tribute and
    /// keeps the player's blockades going. See docs/DESIGN.md §9j.
    /// </summary>
    public class WarSystem : SimSystem
    {
        public override int Order => 32;

        private const int OfferCooldownDays = 15;
        private const int TributeEveryDays = 15;

        // Joined wars end shortly after the war they were joined for. Handled on the next hour, not inside the event.
        private static readonly List<(Faction a, Faction b)> pendingPeace = new List<(Faction, Faction)>();
        private static readonly List<War> buffer = new List<War>();

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        public override void OnWorldLoaded()
        {
            pendingPeace.Clear();
            AdoptOpenWars();
        }

        /// <summary>
        /// Saves from before wars were tracked (and wars declared while Dominion was not watching) get a war record for
        /// every war Dominion knows is active, so they can be won, lost and ended on terms.
        /// </summary>
        private static void AdoptOpenWars()
        {
            WorldComponent_Dominion comp = Comp;
            var factions = new List<Faction>(comp.Cache.Simulated);
            if (Faction.OfPlayerSilentFail != null) factions.Add(Faction.OfPlayer);
            foreach (Faction a in factions)
                foreach (Faction b in factions)
                {
                    if (a == b || !a.HostileTo(b) || comp.Wars.Find(a, b) != null) continue;
                    RelationState r = comp.TryGetRelation(a, b);
                    // The aggressor's side opens the record; for dormant hostility (no start tick) there is no war to adopt.
                    if (r == null || !r.AtWar || !r.wasAggressor) continue;
                    try { OpenWar(a, b, comp.Wars, opening: false); }
                    catch (System.Exception e) { DLog.Warning("Could not adopt the war between " + a.Name + " and " + b.Name + ": " + e.Message); }
                }
        }

        // ---------------------------------------------------------------- events

        public override void OnEvent(DominionEvent ev)
        {
            Faction a = ev.primary, b = ev.secondary;
            if (a == null || b == null) return;
            WarRegistry reg = Comp.Wars;

            if (ev.def == DominionEventDefOf.Dominion_WarDeclared)
                OpenWar(a, b, reg);
            else if (ev.def == DominionEventDefOf.Dominion_PeaceMade)
                CloseWar(a, b, reg);
            else if (ev.def == DominionEventDefOf.Dominion_CapitalLost && a.HostileTo(b))
                WarUtility.AddScore(b, a, 20f);
        }

        private static void OpenWar(Faction a, Faction b, WarRegistry reg, bool opening = true)
        {
            if (!WarUtility.CanWage(a) || !WarUtility.CanWage(b) || reg.Find(a, b) != null) return;
            var w = new War
            {
                id = reg.NewId(),
                attacker = a,
                defender = b,
                startTick = Find.TickManager.TicksGame,
                alliedTo = WarUtility.JoiningFor,
            };
            if (w.alliedTo != null) w.goal = WarGoal.Punitive;
            else if (a.IsPlayer)
            {
                w.goal = reg.pendingPlayerGoal;
                w.goalSettlement = reg.pendingPlayerTarget;
                reg.pendingPlayerGoal = WarGoal.Punitive;
                reg.pendingPlayerTarget = null;
            }
            else w.goal = WarUtility.ChooseGoal(a, b);
            if (w.goal == WarGoal.Conquest && (w.goalSettlement == null || w.goalSettlement.Faction != b))
                w.goalSettlement = b.IsPlayer ? null : WarUtility.NearestSettlement(b, DelegationUtility.HomeTile(a), avoidCapital: true);
            if (w.startTick > 0 && Comp.TryGetRelation(a, b) is RelationState r && r.warSince >= 0) w.startTick = r.warSince;
            reg.wars.Add(w);

            // The opening move: the aggressor marches at once, and the defender often answers in kind.
            if (opening && DominionMod.Settings.warbands)
            {
                WorldComponent_Dominion comp = Comp;
                if (!a.IsPlayer) TryLaunch(a, b, w, comp, force: true);
                if (!b.IsPlayer && Rand.Chance(0.5f)) TryLaunch(b, a, w, comp, force: true);
            }
        }

        private static void CloseWar(Faction a, Faction b, WarRegistry reg)
        {
            War w = reg.Find(a, b);
            if (w == null) return;
            reg.wars.Remove(w);
            WarbandUtility.Recall(a, b);
            Faction loser = w.capitulating;
            string outcome;
            string text = w.deal != null && loser == null ? w.deal.Apply(w, out outcome) : PeaceTerms.Apply(w, loser, out outcome);
            Faction winner = loser != null ? w.Other(loser) : w.deal != null ? null : w.Leader();
            if (loser != null)
                EventBus.Raise(DominionEventDefOf.Dominion_Capitulation, loser, winner, detail: outcome);
            else if (winner != null)
                EventBus.Raise(DominionEventDefOf.Dominion_PeaceTerms, winner, w.Other(winner), detail: outcome);
            if (w.InvolvesPlayer)
                DominionLetters.Send("Dominion_War_EndedLabel".Translate(w.Other(Faction.OfPlayer).Name), text,
                    winner == null ? LetterDefOf.NeutralEvent : winner.IsPlayer ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent,
                    null, w.Other(Faction.OfPlayer), important: true);

            // Wars joined on either side's behalf wind down too.
            foreach (War other in reg.wars)
                if ((other.alliedTo == a && other.Involves(b)) || (other.alliedTo == b && other.Involves(a)))
                    pendingPeace.Add((other.attacker, other.defender));
        }

        /// <summary>Caravans chasing delegations or warbands are steered every slot, and fight when they catch up.</summary>
        public override void Slot(int slot)
        {
            if (Comp.Wars.pursuits.Count > 0) InterceptUtility.Tick();
        }

        // ---------------------------------------------------------------- hourly

        public override void Hourly()
        {
            // Intercepted warbands: score the battle once it is decided.
            List<WarbandFight> fights = Comp.Wars.fights;
            for (int i = fights.Count - 1; i >= 0; i--)
            {
                bool done;
                try { done = fights[i].Check(); }
                catch (System.Exception e)
                {
                    DLog.WarningOnce("Scoring a warband fight failed: " + e.Message, 0x7A55);
                    done = true;
                }
                if (done) fights.RemoveAt(i);
            }

            if (pendingPeace.Count == 0) return;
            var todo = new List<(Faction a, Faction b)>(pendingPeace);
            pendingPeace.Clear();
            foreach ((Faction a, Faction b) in todo)
            {
                if (a == null || b == null || a.defeated || b.defeated || !a.HostileTo(b)) continue;
                if (a.IsPlayer || b.IsPlayer)
                    OfferPeaceToPlayer(a.IsPlayer ? b : a);
                else
                    DiplomacyUtility.SetRelationKind(a, b, FactionRelationKind.Neutral, a, "Dominion_Reason_AllyPeace".Translate());
            }
        }

        private static void OfferPeaceToPlayer(Faction f)
        {
            var node = new DiaNode("Dominion_War_JoinedPeaceText".Translate(f.Name));
            node.options.Add(new DiaOption("Dominion_War_AcceptPeace".Translate())
            {
                action = () =>
                {
                    if (f.HostileTo(Faction.OfPlayer))
                        DiplomacyUtility.SetRelationKind(Faction.OfPlayer, f, FactionRelationKind.Neutral, f, "Dominion_Reason_AllyPeace".Translate());
                },
                resolveTree = true,
            });
            node.options.Add(new DiaOption("Dominion_War_FightOn".Translate()) { resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_War_PeaceOfferTitle".Translate()));
        }

        // ---------------------------------------------------------------- daily

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = Comp;
            WarRegistry reg = comp.Wars;
            foreach (War w in reg.wars)
                if (w.attacker != null && w.defender != null && (w.attacker.defeated || w.defender.defeated))
                    WarbandUtility.Recall(w.attacker, w.defender);
            reg.wars.RemoveAll(w => w.attacker == null || w.defender == null || w.attacker.defeated || w.defender.defeated);
            // A defeated faction's envoys and fighters have nowhere to go.
            foreach (WorldObject o in Find.WorldObjects.AllWorldObjects.ToList())
                if ((o is Warband || o is Delegation) && !o.Destroyed && (o.Faction == null || o.Faction.defeated))
                    o.Destroy();

            foreach (Warband w in WarbandUtility.All().ToList())
                if (w.Blockading) WarbandUtility.BlockadeDay(w);

            PlayerBlockades(reg);
            CollectTribute(comp);

            // Forget occupations whose settlement is gone, or whose original owner is.
            if (reg.originalOwner.Count > 0)
            {
                var alive = new HashSet<int>();
                foreach (Settlement s in Find.WorldObjects.Settlements)
                    if (DominionGeo.OnSurface(s.Tile)) alive.Add(s.Tile.tileId);
                foreach (KeyValuePair<int, int> kv in reg.originalOwner.ToList())
                {
                    Faction orig = comp.Cache.FactionById(kv.Value);
                    if (!alive.Contains(kv.Key) || orig == null || orig.defeated) reg.originalOwner.Remove(kv.Key);
                }
            }
        }

        public override void DailyFaction(Faction f)
        {
            WorldComponent_Dominion comp = Comp;
            WarRegistry reg = comp.Wars;
            buffer.Clear();
            foreach (War w in reg.wars)
                // Each war is handled once a day, in the attacker's slot (the defender's when the player attacked).
                if (w.attacker == f || (w.attacker.IsPlayer && w.defender == f))
                    buffer.Add(w);
            foreach (War w in buffer)
            {
                if (!reg.wars.Contains(w)) continue;
                if (!w.attacker.HostileTo(w.defender))
                {
                    // Ended outside Dominion: no terms, but nobody keeps what they occupied.
                    reg.wars.Remove(w);
                    WarbandUtility.Recall(w.attacker, w.defender);
                    PeaceTerms.ReturnOccupied(w);
                    continue;
                }
                Drift(w);
                if (CheckCapitulation(w, comp)) continue;
                if (DominionMod.Settings.warbands)
                {
                    if (!w.attacker.IsPlayer) TryLaunch(w.attacker, w.defender, w, comp);
                    if (!w.defender.IsPlayer) TryLaunch(w.defender, w.attacker, w, comp);
                }
                if (DominionMod.Settings.warbands)
                {
                    TryReinforce(w.attacker, w.defender, comp);
                    TryReinforce(w.defender, w.attacker, comp);
                }
                if (DominionMod.Settings.aiJointWars && w.DurationTicks > 2 * GenDate.TicksPerDay)
                {
                    if (!w.attacker.IsPlayer) CallAllies(w.attacker, w.defender, w, comp);
                    if (!w.defender.IsPlayer) CallAllies(w.defender, w.attacker, w, comp);
                }
            }
            buffer.Clear();
            PuppetIndependence(f, comp);
        }

        /// <summary>The stronger and less weary side slowly gains ground.</summary>
        private static void Drift(War w)
        {
            float sa = WarUtility.Strength(w.attacker), sd = WarUtility.Strength(w.defender);
            float drift = Mathf.Clamp((sa - sd) / (sa + sd), -1f, 1f) * 0.6f;
            float wa = w.attacker.IsPlayer ? 0f : Comp.GetState(w.attacker).warWeariness;
            float wd = w.defender.IsPlayer ? 0f : Comp.GetState(w.defender).warWeariness;
            drift += (wd - wa) / 100f * 0.4f;
            w.score = Mathf.Clamp(w.score + drift, -100f, 100f);
        }

        /// <summary>A side far behind gives up. The player is asked; the AI decides. True if the war ended.</summary>
        private static bool CheckCapitulation(War w, WorldComponent_Dominion comp)
        {
            Faction winner = w.Leader(DominionMod.Settings.capitulationScore);
            if (winner == null) return false;
            Faction loser = w.Other(winner);
            int now = Find.TickManager.TicksGame;
            if (loser.IsPlayer || winner.IsPlayer)
            {
                if (now - w.lastOfferTick < DominionSettings.AiCooldownTicks(OfferCooldownDays)) return false;
                w.lastOfferTick = now;
                if (loser.IsPlayer) DemandSurrender(w, winner);
                else OfferSurrender(w, loser);
                return false;
            }
            FactionState ls = comp.GetState(loser);
            if (!Rand.Chance(0.25f + ls.warWeariness / 200f + ls.Bloc(Bloc.Doves) * 0.3f)) return false;
            WarUtility.Capitulate(w, loser);
            return !comp.Wars.wars.Contains(w);
        }

        private static void OfferSurrender(War w, Faction loser)
        {
            string terms = PeaceTerms.Describe(w, loser);
            var node = new DiaNode("Dominion_War_SurrenderOfferText".Translate(loser.Name, terms));
            node.options.Add(new DiaOption("Dominion_War_AcceptSurrender".Translate())
            {
                action = () => WarUtility.Capitulate(Comp.Wars.wars.Contains(w) ? w : null, loser),
                resolveTree = true,
            });
            node.options.Add(new DiaOption("Dominion_War_FightOn".Translate()) { resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_War_SurrenderOfferTitle".Translate()));
        }

        private static void DemandSurrender(War w, Faction winner)
        {
            string terms = PeaceTerms.Describe(w, Faction.OfPlayer);
            var node = new DiaNode("Dominion_War_SurrenderDemandText".Translate(winner.Name, terms));
            node.options.Add(new DiaOption("Dominion_War_Surrender".Translate())
            {
                action = () => WarUtility.Capitulate(Comp.Wars.wars.Contains(w) ? w : null, Faction.OfPlayer),
                resolveTree = true,
            });
            node.options.Add(new DiaOption("Dominion_War_Refuse".Translate()) { resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_War_SurrenderDemandTitle".Translate()));
        }

        // ---------------------------------------------------------------- warbands

        /// <summary>Early in a war both sides are eager: launches are this much likelier in the first days.</summary>
        private const int EagerDays = 10;

        private static void TryLaunch(Faction f, Faction enemy, War w, WorldComponent_Dominion comp, bool force = false)
        {
            if (WarbandUtility.CountActive(f) >= WarbandUtility.MaxFor(f)) return;
            FactionState s = comp.GetState(f);
            float chance = (0.05f + 0.12f * s.effectiveAggression) * DominionMod.Settings.warbandFrequency * (1f - s.warWeariness / 150f);
            if (w.DurationTicks < EagerDays * GenDate.TicksPerDay) chance *= 3f;
            WarbandMission mission;
            if (enemy.IsPlayer)
            {
                if (!DominionMod.Settings.warbandRaidsOnPlayer) return;
                if (!Find.Storyteller.difficulty.allowBigThreats) return;
                int now = Find.TickManager.TicksGame;
                DominionSettings set = DominionMod.Settings;
                if (now - s.lastRaidOnPlayerTick < set.raidCooldownDays * GenDate.TicksPerDay) return;
                // A limit on warbands on the way to the colony at once, and a breather between raids from different enemies.
                if (WarbandUtility.All().Count(x => x.mission == WarbandMission.RaidPlayer) >= Mathf.Max(1, set.maxRaidsMarching)) return;
                if (WarbandUtility.All().Any(x => x.Faction == f && x.mission == WarbandMission.RaidPlayer)) return;
                if (set.colonyRaidGapDays > 0)
                    foreach (Faction g in comp.Cache.Simulated)
                        if (now - comp.GetState(g).lastRaidOnPlayerTick < set.colonyRaidGapDays * GenDate.TicksPerDay) return;
                mission = WarbandMission.RaidPlayer;
                chance *= 0.5f;
            }
            else
            {
                if (comp.Cache.SettlementsOf(enemy).Count == 0) return;
                mission = Rand.Chance(0.15f) && !WarbandUtility.All().Any(x => x.Faction == f && x.mission == WarbandMission.Blockade)
                    ? WarbandMission.Blockade : WarbandMission.Siege;
            }
            if (!force && !Rand.Chance(chance)) return;

            Settlement target = null;
            if (mission == WarbandMission.Siege)
            {
                // Conquest goes for its goal; liberation for the towns that were taken; anyone else for the nearest.
                if (w.attacker == f && w.goal == WarGoal.Conquest && w.goalSettlement != null && !w.goalSettlement.Destroyed && w.goalSettlement.Faction == enemy)
                    target = w.goalSettlement;
                else if (w.attacker == f && w.goal == WarGoal.Liberation)
                    target = comp.Cache.SettlementsOf(enemy).FirstOrDefault(x => comp.Wars.OriginalOwnerOf(x) >= 0 && comp.Wars.OriginalOwnerOf(x) != enemy.loadID);
                // An occupied town of ours comes first.
                target ??= comp.Cache.SettlementsOf(enemy).FirstOrDefault(x => comp.Wars.OriginalOwnerOf(x) == f.loadID);
            }
            else if (mission == WarbandMission.Blockade)
                target = comp.GetState(enemy).capital;
            WarbandUtility.Launch(f, enemy, mission, target);
        }

        /// <summary>An ally at peace may still send troops: they march to the ally's capital and stiffen its war effort.</summary>
        private static void TryReinforce(Faction f, Faction enemy, WorldComponent_Dominion comp)
        {
            List<Faction> sim = comp.Cache.Simulated;
            for (int i = 0; i < sim.Count; i++)
            {
                Faction ally = sim[i];
                if (ally == f || ally == enemy || ally.HostileTo(enemy) || f.RelationKindWith(ally) != FactionRelationKind.Ally) continue;
                FactionState s = comp.GetState(ally);
                if (s.activeWars > 0 || WarbandUtility.CountActive(ally) >= WarbandUtility.MaxFor(ally)) continue;
                if (!Rand.Chance(0.02f * (0.3f + s.personality.honour) * DominionMod.Settings.warbandFrequency)) continue;
                WarbandUtility.Launch(ally, enemy, WarbandMission.Reinforce, null, f);
                return;
            }
        }

        // ---------------------------------------------------------------- joint wars

        /// <summary>A faction at war asks its allies and puppets to join. Each is asked once per war.</summary>
        private static void CallAllies(Faction f, Faction enemy, War w, WorldComponent_Dominion comp)
        {
            List<Faction> sim = comp.Cache.Simulated;
            for (int i = 0; i < sim.Count; i++)
                ConsiderAsking(f, enemy, sim[i], w, comp);
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null) ConsiderAsking(f, enemy, player, w, comp);
        }

        private static void ConsiderAsking(Faction f, Faction enemy, Faction candidate, War w, WorldComponent_Dominion comp)
        {
            if (candidate == f || candidate == enemy || w.asked.Contains(candidate.loadID)) return;
            if (candidate.HostileTo(enemy) || candidate.HostileTo(f)) return;
            bool puppet = IsPuppetOf(candidate, f, comp);
            if (f.RelationKindWith(candidate) != FactionRelationKind.Ally && !puppet) return;
            w.asked.Add(candidate.loadID);

            if (candidate.IsPlayer)
            {
                AskPlayer(f, enemy);
                return;
            }
            if (!WarUtility.CanWage(candidate) || candidate.def.permanentEnemy) return;
            FactionState cs = comp.GetState(candidate);
            float chance = puppet ? 0.9f
                : 0.15f + cs.personality.honour * 0.35f + (comp.GetRelation(candidate, enemy).opinion < -20f ? 0.2f : 0f) - cs.activeWars * 0.15f;
            if (comp.Treaties.AnyNonAggression(candidate, enemy)) chance *= 0.3f;
            if (Rand.Chance(Mathf.Clamp01(chance)))
                WarUtility.JoinWar(candidate, f, enemy, "Dominion_Reason_CallToArms".Translate(f.Name));
            else
            {
                comp.GetRelation(f, candidate).favour -= 8f;
                EventBus.Raise(DominionEventDefOf.Dominion_CallToArmsRefused, candidate, f, detail: enemy.Name);
            }
        }

        private static void AskPlayer(Faction ally, Faction enemy)
        {
            var node = new DiaNode("Dominion_War_CallToArmsText".Translate(ally.Name, enemy.Name));
            node.options.Add(new DiaOption("Dominion_War_JoinWar".Translate(enemy.Name))
            {
                action = () =>
                {
                    if (!enemy.HostileTo(Faction.OfPlayer) && !ally.defeated && ally.HostileTo(enemy))
                        WarUtility.JoinWar(Faction.OfPlayer, ally, enemy, "Dominion_Reason_CallToArms".Translate(ally.Name));
                },
                resolveTree = true,
            });
            node.options.Add(new DiaOption("Dominion_War_DeclineCall".Translate())
            {
                action = () =>
                {
                    Comp.GetRelation(ally, Faction.OfPlayer).favour -= 15f;
                    EventBus.Raise(DominionEventDefOf.Dominion_CallToArmsRefused, Faction.OfPlayer, ally, detail: enemy.Name);
                },
                resolveTree = true,
            });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_War_CallToArmsTitle".Translate()));
        }

        public static bool IsPuppetOf(Faction puppet, Faction overlord, WorldComponent_Dominion comp)
        {
            Treaty t = comp.Treaties.Find(puppet, overlord, DominionTreatyDefOf.Dominion_Puppet);
            return t != null && t.a == puppet;
        }

        /// <summary>A puppet that has come to resent its overlord, and is steady enough, may break free.</summary>
        private static void PuppetIndependence(Faction f, WorldComponent_Dominion comp)
        {
            IReadOnlyList<Treaty> all = comp.Treaties.All;
            for (int i = 0; i < all.Count; i++)
            {
                Treaty t = all[i];
                if (!t.def.puppet || t.a != f) continue;
                Faction overlord = t.b;
                FactionState s = comp.GetState(f);
                if (comp.GetRelation(f, overlord).opinion > -25f || s.stability < 45f || !Rand.Chance(0.02f)) continue;
                comp.Treaties.Break(t, f);
                EventBus.Raise(DominionEventDefOf.Dominion_PuppetFreed, f, overlord);
                if (!overlord.IsPlayer && DiplomacyUtility.AiMayChange(overlord, f) && Rand.Chance(comp.GetState(overlord).personality.aggression * 0.5f))
                    DiplomacyUtility.SetRelationKind(overlord, f, FactionRelationKind.Hostile, overlord, "Dominion_Reason_PuppetRevolt".Translate());
                return;
            }
        }

        // ---------------------------------------------------------------- tribute and blockades

        private static void CollectTribute(WorldComponent_Dominion comp)
        {
            int now = Find.TickManager.TicksGame;
            int every = TributeEveryDays * GenDate.TicksPerDay;
            foreach (Treaty t in comp.Treaties.All.ToList())
            {
                if (t.def.tribute <= 0f) continue;
                if (now - Mathf.Max(t.signedTick, t.lastPaymentTick) < every) continue;
                t.lastPaymentTick = now;
                if (!t.a.IsPlayer && !t.b.IsPlayer)
                {
                    // Between AI factions the tribute is real silver from one E&D stockpile to the other.
                    EconomyEffects.Transfer(t.a, t.b, Mathf.RoundToInt(Mathf.Clamp(comp.GetState(t.a).wealth * 0.003f, 100f, 600f)));
                    continue;
                }
                if (t.b.IsPlayer)
                {
                    FactionState s = comp.GetState(t.a);
                    int amount = EconomyEffects.Transfer(t.a, t.b, Mathf.RoundToInt(Mathf.Clamp(s.wealth * 0.003f, 100f, 600f)));
                    if (amount > 0)
                    {
                        EventBus.Raise(DominionEventDefOf.Dominion_TributePaid, t.a, t.b, detail: amount.ToString());
                        Messages.Message("Dominion_Tribute_Arrived".Translate(t.a.Name, amount), MessageTypeDefOf.PositiveEvent, historical: true);
                    }
                }
                else
                {
                    int amount = Mathf.RoundToInt(Mathf.Clamp(WarUtility.PlayerHomeWealth() * 0.005f, 150f, 1000f));
                    if (DiplomacyHelpers.AvailableSilverFresh() < amount)
                    {
                        comp.Treaties.Break(t, Faction.OfPlayer);
                        DominionLetters.Send("Dominion_Tribute_DefaultLabel".Translate(), "Dominion_Tribute_DefaultText".Translate(t.b.Name, amount),
                            LetterDefOf.NegativeEvent, null, t.b);
                        continue;
                    }
                    EconomyEffects.Transfer(t.a, t.b, amount);
                    EventBus.Raise(DominionEventDefOf.Dominion_TributePaid, t.a, t.b, detail: amount.ToString());
                    Messages.Message("Dominion_Tribute_PaidByPlayer".Translate(amount, t.b.Name), MessageTypeDefOf.NeutralEvent, historical: true);
                }
            }
        }

        private static void PlayerBlockades(WarRegistry reg)
        {
            for (int i = reg.blockades.Count - 1; i >= 0; i--)
            {
                Blockade b = reg.blockades[i];
                if (!b.StillValid)
                {
                    reg.blockades.RemoveAt(i);
                    if (b.settlement != null && !b.settlement.Destroyed)
                        Messages.Message("Dominion_Blockade_Lifted".Translate(b.settlement.Label), MessageTypeDefOf.NeutralEvent, historical: false);
                    continue;
                }
                Faction f = b.settlement.Faction;
                // The blockade itself is in the economic climate (E&D production falls); spoiled goods on top.
                EconomyEffects.DestroyGoods(f, 150f);
                WarUtility.AddScore(Faction.OfPlayer, f, 1.5f);
                // The garrison may sally out.
                if (Rand.Chance(0.1f)) Sortie(b);
            }
        }

        public static void StartBlockade(Caravan caravan, Settlement settlement)
        {
            WarRegistry reg = Comp.Wars;
            if (reg.blockades.Any(x => x.settlement == settlement && x.StillValid)) return;
            var b = new Blockade { caravan = caravan, settlement = settlement, startTick = Find.TickManager.TicksGame };
            if (!b.StillValid) return;
            reg.blockades.Add(b);
            EventBus.Raise(DominionEventDefOf.Dominion_Blockade, Faction.OfPlayer, settlement.Faction, settlement.Tile.tileId, detail: settlement.Label);
            Messages.Message("Dominion_Blockade_Started".Translate(settlement.Label), caravan, MessageTypeDefOf.NeutralEvent, historical: true);
        }

        private static void Sortie(Blockade b)
        {
            Faction f = b.settlement.Faction;
            Comp.Wars.blockades.Remove(b);
            float pts = Mathf.Max(WarbandUtility.ForPlayerBattle(f, Mathf.Min(WarbandUtility.FieldPoints(f) * 0.5f, 2000f), WarbandUtility.SortieShare),
                f.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat));
            var parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                faction = f,
                tile = b.caravan.Tile,
                points = pts,
                generateFightersOnly = true,
            };
            List<Pawn> enemies = PawnGroupMakerUtility.GeneratePawns(parms, false).ToList();
            if (enemies.Count == 0) return;
            Map map = CaravanIncidentUtility.SetupCaravanAttackMap(b.caravan, enemies, true);
            LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, true, true, false, false, true, false, true), map, enemies);
            DominionLetters.Send("Dominion_Blockade_SortieLabel".Translate(), "Dominion_Blockade_SortieText".Translate(b.settlement.Label, f.Name),
                LetterDefOf.ThreatBig, new LookTargets(enemies), f, important: true);
        }
    }

    /// <summary>Player caravans at an enemy settlement can blockade it.</summary>
    public class WorldObjectCompProperties_Blockade : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_Blockade()
        {
            compClass = typeof(WorldObjectComp_Blockade);
        }
    }

    public class WorldObjectComp_Blockade : WorldObjectComp
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            if (!(parent is Settlement s) || s.Faction == null || s.Faction.IsPlayer || !caravan.IsPlayerControlled) yield break;
            if (!s.Faction.HostileTo(Faction.OfPlayer) || !WarUtility.CanWage(s.Faction)) yield break;
            foreach (FloatMenuOption o in CaravanArrivalActionUtility.GetFloatMenuOptions(
                         () => CaravanArrivalAction_Blockade.CanBlockade(caravan, s),
                         () => new CaravanArrivalAction_Blockade(s),
                         "Dominion_Blockade_Option".Translate(s.Label), caravan, s.Tile, s, null))
                yield return o;
        }
    }

    public class CaravanArrivalAction_Blockade : CaravanArrivalAction
    {
        private Settlement settlement;

        public CaravanArrivalAction_Blockade() { }

        public CaravanArrivalAction_Blockade(Settlement settlement)
        {
            this.settlement = settlement;
        }

        public override string Label => "Dominion_Blockade_Option".Translate(settlement?.Label ?? "?");

        public override string ReportString => "Dominion_Blockade_Report".Translate(settlement?.Label ?? "?");

        public static FloatMenuAcceptanceReport CanBlockade(Caravan caravan, Settlement s)
        {
            if (s == null || s.Destroyed || s.Faction == null || !s.Faction.HostileTo(Faction.OfPlayer)) return false;
            if (WorldComponent_Dominion.Instance.Wars.blockades.Any(b => b.settlement == s && b.StillValid))
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Blockade_Already".Translate());
            if (!caravan.PawnsListForReading.Any(p => p.IsColonist && !p.Downed))
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Blockade_NoFighters".Translate());
            return true;
        }

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            if (settlement == null || settlement.Tile != destinationTile) return false;
            return CanBlockade(caravan, settlement);
        }

        public override void Arrived(Caravan caravan)
        {
            if (CanBlockade(caravan, settlement).Accepted) WarSystem.StartBlockade(caravan, settlement);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
        }
    }
}
