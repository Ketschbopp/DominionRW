using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The seat of the player's government. The head of state works here to earn influence; the desk unlocks
    /// full diplomacy. Its material sets its tier: wood, steel (faster) or gold (faster, and more persuasive).
    /// </summary>
    public class Building_LeadershipDesk : Building
    {
        public DeskTier Tier
        {
            get
            {
                if (Stuff == ThingDefOf.Gold) return DeskTier.Gold;
                if (Stuff == ThingDefOf.Steel) return DeskTier.Steel;
                return DeskTier.Wood;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            if (Faction != Faction.OfPlayer) yield break;

            if (!LeadershipUtility.UsesIdeoLeader)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dominion_Desk_ChooseHeadOfState".Translate(),
                    defaultDesc = "Dominion_Desk_ChooseHeadOfState_Desc".Translate(),
                    icon = DominionTex.HeadOfState,
                    action = ChooseHeadOfState,
                };
            }

            if (!DiplomatUtility.UsesIdeoRoles)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dominion_Desk_AssignDiplomats".Translate(),
                    defaultDesc = "Dominion_Desk_AssignDiplomats_Desc".Translate(),
                    icon = DominionTex.Diplomacy,
                    action = AssignDiplomats,
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "Dominion_Desk_MakeCapital".Translate(),
                defaultDesc = "Dominion_Desk_MakeCapital_Desc".Translate(),
                icon = DominionTex.Capital,
                action = () =>
                {
                    if (Map?.Parent is Settlement s)
                    {
                        WorldComponent_Dominion.Instance.Player.capital = s;
                        Messages.Message("Dominion_Desk_CapitalSet".Translate(s.Label), this, MessageTypeDefOf.PositiveEvent, historical: false);
                    }
                },
            };

            yield return new Command_Action
            {
                defaultLabel = "Dominion_Desk_OpenDiplomacy".Translate(),
                defaultDesc = "Dominion_Desk_OpenDiplomacy_Desc".Translate(),
                icon = DominionTex.Diplomacy,
                action = () => Find.MainTabsRoot.SetCurrentTab(DominionMainButtonDefOf.Dominion, true),
            };
        }

        private void ChooseHeadOfState()
        {
            var options = new List<FloatMenuOption>();
            foreach (Pawn p in Map.mapPawns.FreeColonistsSpawned)
            {
                Pawn captured = p;
                int social = p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                options.Add(new FloatMenuOption(p.LabelShortCap + " (" + SkillDefOf.Social.LabelCap + " " + social + ")",
                    () => WorldComponent_Dominion.Instance.Player.chosenHeadOfState = captured));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Dominion_Desk_NoColonists".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void AssignDiplomats()
        {
            List<Pawn> chosen = WorldComponent_Dominion.Instance.Player.chosenDiplomats;
            var options = new List<FloatMenuOption>();
            foreach (Pawn p in Map.mapPawns.FreeColonistsSpawned)
            {
                Pawn captured = p;
                bool isDiplomat = chosen.Contains(p);
                int social = p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                string label = (isDiplomat ? "✓ " : "") + p.LabelShortCap + " (" + SkillDefOf.Social.LabelCap + " " + social + ")";
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (chosen.Contains(captured)) chosen.Remove(captured);
                    else chosen.Add(captured);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Dominion_Desk_NoColonists".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Dominion_Desk_Tier".Translate(("Dominion_DeskTier_" + Tier).Translate()));
            Pawn hos = LeadershipUtility.HeadOfState();
            sb.AppendLine();
            sb.Append("Dominion_Desk_HeadOfState".Translate(hos != null ? hos.LabelShortCap : LeadershipUtility.NoHeadOfStateReason()));
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp != null)
            {
                sb.AppendLine();
                sb.Append("Dominion_Desk_Influence".Translate(comp.Player.influence.ToString("F0"), PlayerState.MaxInfluence.ToString("F0")));
            }
            return sb.ToString();
        }
    }

    [StaticConstructorOnStartup]
    public static class DominionTex
    {
        public static readonly Texture2D HeadOfState = ContentFinder<Texture2D>.Get("UI/Commands/Dominion_HeadOfState");
        public static readonly Texture2D Capital = ContentFinder<Texture2D>.Get("UI/Commands/Dominion_Capital");
        public static readonly Texture2D Diplomacy = ContentFinder<Texture2D>.Get("UI/Commands/Dominion_Diplomacy");
    }

    [DefOf]
    public static class DominionMainButtonDefOf
    {
        public static MainButtonDef Dominion;

        static DominionMainButtonDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionMainButtonDefOf));
        }
    }
}
