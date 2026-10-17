using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace Dominion
{
    /// <summary>
    /// The Courier's extras on top of a story: a colonist's reaction (their temper picked from their traits),
    /// an occasional street-level aside, and a "what it means for us" box written from the actual game state.
    /// All of it is resolved once at delivery and saved with the item.
    /// </summary>
    public static class NewsFlavor
    {
        private const float AsideChance = 0.45f;

        /// <summary>Rough caravan speed for the "days away" hint: tiles per day on average terrain.</summary>
        private const float TilesPerDay = 14f;

        private static readonly List<Pawn> candidates = new List<Pawn>();

        /// <summary>Adds the aside and a colonist's quote to the story. Uses the same request as the story, so all its symbols work.</summary>
        public static void Decorate(NewsItem n, GrammarRequest baseReq)
        {
            RulePackDef vox = DefDatabase<RulePackDef>.GetNamedSilentFail("Dominion_NewsVox");
            RulePackDef voxEvents = DefDatabase<RulePackDef>.GetNamedSilentFail("Dominion_NewsVoxEvents");
            if (vox == null || n.story.NullOrEmpty() || n.def == null) return;
            string ev = n.def.defName.StartsWith("Dominion_") ? n.def.defName.Substring("Dominion_".Length) : n.def.defName;

            Rand.PushState(n.id * 31337 + 5);
            try
            {
                GrammarRequest req = default;
                req.Includes.AddRange(baseReq.Includes);
                req.Includes.Add(vox);
                if (voxEvents != null) req.Includes.Add(voxEvents);
                req.Rules.AddRange(baseReq.Rules);
                req.Constants["CAT"] = n.Category.ToString();
                req.Constants["US"] = IntelUtility.InvolvesPlayer(n.primary, n.secondary) ? "yes" : "no";
                req.Constants["RUMOUR"] = n.confidence == NewsConfidence.Rumor ? "yes" : "no";
                req.Rules.Add(new Rule_String("COLONY", ColonyName()));

                var sb = new StringBuilder(n.story);
                if (Rand.Chance(AsideChance))
                {
                    req.Constants["TONE"] = "plain";
                    string aside = Fresh(req, Root("aside", ev, voxEvents), "Dominion news aside");
                    if (!aside.NullOrEmpty()) sb.Append(" ").Append(aside);
                }

                Pawn speaker = PickSpeaker();
                if (speaker != null)
                {
                    req.Constants["TONE"] = ToneOf(speaker);
                    req.Rules.Add(new Rule_String("COLONIST", speaker.LabelShort));
                    string quote = Fresh(req, Root("vox", ev, voxEvents), "Dominion news vox");
                    if (!quote.NullOrEmpty())
                        sb.Append("\n\n").Append("Dominion_News_Vox".Translate(quote, speaker.LabelShort, RoleOf(speaker), ColonyName()).Resolve());
                }
                n.story = sb.ToString();
            }
            catch (System.Exception e)
            {
                DLog.WarningOnce("News flavour failed: " + e.Message, 0x7F1A);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// The event's own lines ("vox_WarDeclared") most of the time when it has any, so quotes fit the story;
        /// otherwise the shared lines by category and temper.
        /// </summary>
        private static string Root(string root, string ev, RulePackDef voxEvents)
        {
            string specific = root + "_" + ev;
            if (voxEvents == null || !HasRoot(voxEvents, specific)) return root;
            return Rand.Chance(0.8f) ? specific : root;
        }

        private static readonly Dictionary<string, bool> rootCache = new Dictionary<string, bool>();

        private static bool HasRoot(RulePackDef pack, string root)
        {
            if (rootCache.TryGetValue(root, out bool has)) return has;
            has = false;
            foreach (Rule r in pack.RulesPlusIncludes)
                if (r.keyword == root)
                {
                    has = true;
                    break;
                }
            rootCache[root] = has;
            return has;
        }

        /// <summary>Recently printed lines, so the same quote does not turn up story after story.</summary>
        private static readonly Queue<string> recent = new Queue<string>();
        private static readonly HashSet<string> recentSet = new HashSet<string>();
        private const int RecentMemory = 60;

        private static string Fresh(GrammarRequest req, string root, string debugLabel)
        {
            string line = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                line = GrammarResolver.Resolve(root, req, debugLabel, false, null, null, null, true);
                if (line.NullOrEmpty() || !recentSet.Contains(line)) break;
                // Fall back to the shared pool after a few repeats from a small event pool.
                if (attempt == 3 && root.Contains("_")) root = root.Substring(0, root.IndexOf('_'));
            }
            if (!line.NullOrEmpty() && recentSet.Add(line))
            {
                recent.Enqueue(line);
                if (recent.Count > RecentMemory) recentSet.Remove(recent.Dequeue());
            }
            return line;
        }

        private static Pawn PickSpeaker()
        {
            candidates.Clear();
            foreach (Map m in Find.Maps)
                if (m.IsPlayerHome)
                    foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
                        if (p.RaceProps.Humanlike && !p.DevelopmentalStage.Baby() && (p.health?.capacities?.CapableOf(PawnCapacityDefOf.Talking) ?? false))
                            candidates.Add(p);
            Pawn picked = candidates.Count > 0 ? candidates.RandomElement() : null;
            candidates.Clear();
            return picked;
        }

        /// <summary>The colonist's temper, matched against TONE in the vox grammar.</summary>
        public static string ToneOf(Pawn p)
        {
            TraitSet traits = p.story?.traits;
            if (traits == null) return "plain";
            if (traits.HasTrait(TraitDefOf.Psychopath) || traits.HasTrait(TraitDefOf.Bloodlust)) return "callous";
            if (traits.HasTrait(TraitDefOf.Kind)) return "kind";
            if (traits.HasTrait(TraitDefOf.Greedy) || traits.HasTrait(TraitDefOf.Jealous)) return "greedy";
            TraitDef mood = DefDatabase<TraitDef>.GetNamedSilentFail("NaturalMood");
            if (mood != null && traits.HasTrait(mood))
            {
                int degree = traits.DegreeOfTrait(mood);
                if (degree < 0) return "grim";
                if (degree > 0) return "sunny";
            }
            if (traits.HasTrait(TraitDefOf.Joyous)) return "sunny";
            return "plain";
        }

        /// <summary>"our leader", "our cook", ...: the head of state, else their best skill.</summary>
        public static string RoleOf(Pawn p)
        {
            if (p == LeadershipUtility.HeadOfState()) return "Dominion_Vox_Role_Leader".Translate();
            SkillDef best = null;
            int level = -1;
            if (p.skills != null)
                foreach (SkillRecord s in p.skills.skills)
                    if (!s.TotallyDisabled && s.Level > level)
                    {
                        level = s.Level;
                        best = s.def;
                    }
            if (best == null || level < 4) return "Dominion_Vox_Role_Colonist".Translate();
            string key = "Dominion_Vox_Role_" + best.defName;
            return key.CanTranslate() ? key.Translate() : "Dominion_Vox_Role_Colonist".Translate();
        }

        public static string ColonyName()
        {
            Map home = Find.AnyPlayerHomeMap;
            return home?.Parent?.LabelCap ?? Faction.OfPlayerSilentFail?.Name ?? "Dominion_Vox_OurColony".Translate().ToString();
        }

        // ------------------------------------------------------------------ what it means for us

        /// <summary>
        /// Practical notes for the player, from the state of the world when the news arrived: how far away the
        /// factions are and how they stand with us, what the event does to them, and what the colony can do about it.
        /// </summary>
        public static string Advice(NewsItem n)
        {
            Faction player = Faction.OfPlayerSilentFail;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (player == null || comp == null || n.def == null || n.correctsId >= 0) return null;
            var lines = new List<string>();

            foreach (Faction f in new[] { n.primary, n.secondary })
            {
                if (f == null || f.IsPlayer || f.defeated) continue;
                lines.Add(Standing(f, player, comp));
            }

            Faction a = n.primary != null && !n.primary.IsPlayer ? n.primary : n.secondary != null && !n.secondary.IsPlayer ? n.secondary : null;
            bool near = Near(n.primary, comp, player) || Near(n.secondary, comp, player);
            bool allyInvolved = IsAlly(n.primary, player) || IsAlly(n.secondary, player);

            string nuclear = NuclearAdvice(n, player);
            if (nuclear != null) lines.Add(nuclear);
            else switch (n.Category)
            {
                case EventCategory.War:
                case EventCategory.Military:
                    if (IntelUtility.InvolvesPlayer(n.primary, n.secondary)) lines.Add("Dominion_Advice_WarUs".Translate());
                    else if (allyInvolved) lines.Add("Dominion_Advice_WarAlly".Translate());
                    else if (near) lines.Add("Dominion_Advice_WarNear".Translate());
                    else lines.Add("Dominion_Advice_WarFar".Translate());
                    break;
                case EventCategory.Economy:
                case EventCategory.Disaster:
                    if (a != null) AddEconomy(lines, a, comp);
                    break;
                case EventCategory.Politics:
                    if (a != null && comp.TryGetState(a, out FactionState ps))
                        lines.Add((ps.stability < 35f ? "Dominion_Advice_Unstable" : "Dominion_Advice_Stable").Translate(a.Name, ps.stability.ToString("F0")));
                    break;
                case EventCategory.Espionage:
                    lines.Add("Dominion_Advice_Espionage".Translate());
                    break;
                case EventCategory.Diplomacy:
                    if (n.def == DominionEventDefOf.Dominion_PeaceMade) lines.Add("Dominion_Advice_Peace".Translate());
                    break;
            }
            if (n.def.defName == "Dominion_Sabotage" && a != null) AddEconomy(lines, a, comp);

            // Pointers to what the colony can actually do.
            ThoughtDef worry = NewsThoughts.ThoughtFor(n);
            if (worry != null && worry.stages[0].baseMoodEffect < 0f && DominionMod.Settings.colonistNewsThoughts)
                lines.Add(comp.RadioOrders.AdvisoryInForce ? "Dominion_Advice_MoodShielded".Translate() : "Dominion_Advice_Mood".Translate(worry.stages[0].label));

            if (lines.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (string l in lines)
                sb.AppendLine("• " + l);
            return sb.ToString().TrimEnd();
        }

        /// <summary>Strategic weapons reach everyone however far away they fall: what each stage of IRBM's war means for us.</summary>
        private static string NuclearAdvice(NewsItem n, Faction player)
        {
            switch (n.def.defName)
            {
                case "Dominion_MissileStrike":
                    if (n.secondary == player) return "Dominion_Advice_MissileOnUs".Translate();
                    if (n.primary == player) return "Dominion_Advice_MissileByUs".Translate();
                    return "Dominion_Advice_Missile".Translate();
                case "Dominion_DoomsdayClock": return "Dominion_Advice_Doomsday".Translate();
                case "Dominion_NuclearWar": return "Dominion_Advice_NuclearWar".Translate();
                case "Dominion_NuclearWinter": return "Dominion_Advice_NuclearWinter".Translate(n.detail ?? "?");
                case "Dominion_NuclearWinterEnds": return "Dominion_Advice_NuclearWinterEnds".Translate();
                default: return null;
            }
        }

        private static string Standing(Faction f, Faction player, WorldComponent_Dominion comp)
        {
            string rel;
            switch (f.RelationKindWith(player))
            {
                case FactionRelationKind.Ally: rel = "Dominion_Advice_RelAlly".Translate(); break;
                case FactionRelationKind.Hostile: rel = "Dominion_Advice_RelHostile".Translate(); break;
                default: rel = "Dominion_Advice_RelNeutral".Translate(); break;
            }
            RelationState r = comp.TryGetRelation(f, player);
            string where = r != null && r.distance >= 0f
                ? "Dominion_Advice_Days".Translate(Mathf.Max(0.5f, Mathf.Round(r.distance / TilesPerDay * 2f) / 2f).ToString("0.#"))
                : "Dominion_Advice_DaysUnknown".Translate();
            return "Dominion_Advice_Standing".Translate(f.Name, rel, f.PlayerGoodwill.ToStringWithSign(), where);
        }

        private static void AddEconomy(List<string> lines, Faction f, WorldComponent_Dominion comp)
        {
            float factor = EconomyEffects.ProductionFactor(f);
            if (factor < 0.97f)
                lines.Add("Dominion_Advice_EconomyDown".Translate(f.Name, factor.ToStringPercent()));
            else
                lines.Add("Dominion_Advice_EconomyOk".Translate(f.Name));
            if (comp.TryGetState(f, out FactionState s) && s.InCrisis)
                lines.Add("Dominion_Advice_Crisis".Translate(f.Name, (s.crisisUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
        }

        private static bool IsAlly(Faction f, Faction player) => f != null && !f.IsPlayer && f.RelationKindWith(player) == FactionRelationKind.Ally;

        private static bool Near(Faction f, WorldComponent_Dominion comp, Faction player)
        {
            if (f == null || f.IsPlayer) return false;
            RelationState r = comp.TryGetRelation(f, player);
            return r != null && r.distance >= 0f && r.distance < NewsThoughts.NearbyDistance;
        }
    }
}
