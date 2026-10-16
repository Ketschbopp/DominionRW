using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The dossier under the overview table: profile, internal politics and how the faction sees everyone else.
    /// In M2 what is shown here will depend on the player's intel; for now everything is visible.
    /// </summary>
    public static class FactionDossierPanel
    {
        private const float Line = 22f;

        private static readonly List<RelationRow> rows = new List<RelationRow>();
        private static readonly List<Faction> others = new List<Faction>();

        private struct RelationRow
        {
            public Faction other;
            public RelationState rel;
        }

        public static void Draw(Rect rect, Faction f, WorldComponent_Dominion comp, ref Vector2 scroll)
        {
            Widgets.DrawMenuSection(rect);
            rect = rect.ContractedBy(8f);
            if (!comp.TryGetState(f, out FactionState s) || !s.governmentInitialized)
            {
                Widgets.Label(rect, "Dominion_Dossier_NotYetSimulated".Translate());
                return;
            }

            float colWidth = (rect.width - 20f) / 3f;
            Rect profile = new Rect(rect.x, rect.y, colWidth, rect.height);
            Rect politics = new Rect(profile.xMax + 10f, rect.y, colWidth, rect.height);
            Rect relations = new Rect(politics.xMax + 10f, rect.y, colWidth, rect.height);

            DrawProfile(profile, f, s);
            DrawPolitics(politics, s);
            DrawRelations(relations, f, comp, ref scroll);
        }

        private static void DrawProfile(Rect rect, Faction f, FactionState s)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);
            Text.Font = GameFont.Medium;
            l.Label(f.Name.Truncate(rect.width));
            Text.Font = GameFont.Small;
            int now = Find.TickManager.TicksGame;
            string leader = f.leader != null
                ? f.leader.LabelShortCap + (s.leaderSinceTick >= 0 ? " (" + (now - s.leaderSinceTick).ToStringTicksToPeriod(allowSeconds: false) + ")" : "")
                : "-";
            Row(l, "Dominion_Dossier_Leader".Translate(), leader);
            Row(l, "Dominion_Dossier_Capital".Translate(), s.capital?.LabelCap ?? "Dominion_None".Translate());
            Row(l, "Dominion_Col_Government".Translate(), DominionUI.GovernmentLabel(s.government));
            Row(l, "Dominion_Col_Stability".Translate(), DominionUI.Colored(s.stability.ToString("F0"), DominionUI.StabilityColor(s.stability)));
            Row(l, "Dominion_Dossier_Legitimacy".Translate(), s.legitimacy.ToString("F0"));
            Row(l, "Dominion_Dossier_WarWeariness".Translate(), s.warWeariness.ToString("F0"));
            Row(l, "Dominion_Dossier_ActiveWars".Translate(), s.activeWars.ToString());
            Row(l, "Dominion_Dossier_Population".Translate(), s.population + " / " + s.totalLiving);
            Row(l, "Dominion_Dossier_Wealth".Translate(), s.wealth.ToStringMoney());
            Row(l, "Dominion_Col_Economy".Translate(), s.economyIndex.ToString("F0"));
            Row(l, "Dominion_Col_Military".Translate(), s.militaryStrength.ToString("F0"));
            Row(l, "Dominion_Dossier_Territory".Translate(), s.territoryTiles + " / " + s.settlementCount);
            Row(l, "Dominion_Dossier_VassalOutposts".Translate(), s.vassalOutposts.ToString());
            l.End();
        }

        private static void Row(Listing_Standard l, string label, string value)
        {
            Rect r = l.GetRect(Line);
            GUI.color = Color.gray;
            Widgets.Label(r.LeftPart(0.48f), label);
            GUI.color = Color.white;
            Widgets.Label(r.RightPart(0.52f), value);
        }

        private static void DrawPolitics(Rect rect, FactionState s)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);
            l.Label("Dominion_Dossier_Personality".Translate());
            AIPersonality p = s.personality;
            DominionUI.Bar(l.GetRect(Line), "Dominion_Trait_Aggression".Translate(), p.aggression, p.aggression.ToStringPercent());
            DominionUI.Bar(l.GetRect(Line), "Dominion_Trait_Greed".Translate(), p.greed, p.greed.ToStringPercent());
            DominionUI.Bar(l.GetRect(Line), "Dominion_Trait_Honour".Translate(), p.honour, p.honour.ToStringPercent());
            DominionUI.Bar(l.GetRect(Line), "Dominion_Trait_Paranoia".Translate(), p.paranoia, p.paranoia.ToStringPercent());
            l.Gap(8f);
            l.Label("Dominion_Dossier_Blocs".Translate());
            for (int i = 0; i < FactionState.BlocCount; i++)
            {
                Bloc b = (Bloc)i;
                Rect r = l.GetRect(Line);
                DominionUI.Bar(r, DominionUI.BlocLabel(b), s.blocs[i], s.blocs[i].ToStringPercent());
                TooltipHandler.TipRegion(r, ("Dominion_Bloc_" + b + "_Desc").Translate());
            }
            l.End();
        }

        private static void DrawRelations(Rect rect, Faction f, WorldComponent_Dominion comp, ref Vector2 scroll)
        {
            Rect title = new Rect(rect.x, rect.y, rect.width, Line);
            Widgets.Label(title, "Dominion_Dossier_Opinions".Translate());

            rows.Clear();
            others.Clear();
            others.AddRange(comp.Cache.Simulated);
            if (Faction.OfPlayerSilentFail != null) others.Add(Faction.OfPlayer);
            foreach (Faction other in others)
            {
                if (other == f) continue;
                RelationState r = comp.TryGetRelation(f, other);
                if (r != null) rows.Add(new RelationRow { other = other, rel = r });
            }
            rows.SortByDescending(x => x.rel.opinion);

            Rect outRect = new Rect(rect.x, rect.y + Line, rect.width, rect.height - Line);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, rows.Count * Line);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < rows.Count; i++)
            {
                RelationRow row = rows[i];
                Rect r = new Rect(0f, i * Line, view.width, Line);
                if (i % 2 == 1) Widgets.DrawLightHighlight(r);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(r.x, r.y + 1f, 20f, 20f), row.other);
                string tag = "";
                FactionRelationKind kind = f.RelationKindWith(row.other);
                if (row.rel.AtWar) tag = " [" + "Dominion_Tag_War".Translate() + "]";
                else if (kind == FactionRelationKind.Hostile) tag = " [" + "Dominion_Tag_Hostile".Translate() + "]";
                else if (kind == FactionRelationKind.Ally) tag = " [" + "Dominion_Tag_Ally".Translate() + "]";
                else if (now < row.rel.truceUntil) tag = " [" + "Dominion_Tag_Truce".Translate() + "]";
                Rect nameRect = new Rect(r.x + 24f, r.y, r.width - 24f - 50f, Line);
                Widgets.Label(nameRect, (row.other.Name + tag).Truncate(nameRect.width));
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(r.xMax - 50f, r.y, 50f, Line), DominionUI.Colored(row.rel.opinion.ToString("F0"), DominionUI.OpinionColor(row.rel.opinion)));
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(r, Breakdown(row.rel));
            }
            Widgets.EndScrollView();
        }

        private static string Breakdown(RelationState r) =>
            "Dominion_Dossier_OpinionBreakdown".Translate(
                r.opinion.ToString("F0"),
                r.targetOpinion.ToString("F0"),
                r.ideoCompatibility.ToStringPercent(),
                r.distance < 0f ? "?" : r.distance.ToString("F0"),
                r.sharedEnemies,
                r.grievance.ToString("F0"),
                r.outpostsHeldByOther);
    }
}
