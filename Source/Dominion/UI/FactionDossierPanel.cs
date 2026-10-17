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
        private static Faction rowsFaction;
        private static float rowsNextTime;

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
            if (IntelUtility.Knows(f, IntelUtility.SecretIntel)) DrawPolitics(politics, s);
            else Locked(politics, "Dominion_Dossier_Personality".Translate(), IntelUtility.SecretIntel);
            if (IntelUtility.Knows(f, IntelUtility.BlackOpsIntel)) DrawRelations(relations, f, comp, ref scroll);
            else Locked(relations, "Dominion_Dossier_Opinions".Translate(), IntelUtility.BlackOpsIntel);
        }

        private static Vector2 profileScroll;
        private static float profileHeight = 400f;
        private static Faction profileFaction;

        // The profile has more rows than fit at small window sizes: it scrolls.
        private static void DrawProfile(Rect outer, Faction f, FactionState s)
        {
            if (profileFaction != f)
            {
                profileFaction = f;
                profileScroll = Vector2.zero;
            }
            Rect rect = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(profileHeight, outer.height));
            Widgets.BeginScrollView(outer, ref profileScroll, rect);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);
            Text.Font = GameFont.Medium;
            l.Label(f.Name.Truncate(rect.width));
            Text.Font = GameFont.Small;
            int now = Find.TickManager.TicksGame;
            string leader = f.leader != null
                ? f.leader.LabelShortCap + (s.leaderSinceTick >= 0 ? " (" + (now - s.leaderSinceTick).ToStringTicksToPeriod(allowSeconds: false) + ")" : "")
                : "-";
            float intel = IntelUtility.IntelOn(f);
            Row(l, "Dominion_Dossier_Intel".Translate(), intel.ToString("F0"));
            Row(l, "Dominion_Dossier_Culture".Translate(), s.playerCulture.ToString("F0"));
            Row(l, "Dominion_Dossier_Leader".Translate(), leader);
            Row(l, "Dominion_Dossier_Capital".Translate(), s.capital?.LabelCap ?? "Dominion_None".Translate());
            var leadership = IntegrationRegistry.Get<Integration_SimpleLeadership>();
            if (leadership != null && leadership.IsActive && s.capital != null)
                Row(l, "Dominion_Dossier_Governor".Translate(), f, IntelUtility.RestrictedIntel,
                    () => leadership.GovernorOf(s.capital)?.LabelShortCap ?? "Dominion_None".Translate().ToString());
            Row(l, "Dominion_Col_Government".Translate(), DominionUI.GovernmentLabel(s.government));
            TooltipHandler.TipRegion(new Rect(0f, l.CurHeight - Line, l.ColumnWidth, Line), () => GovernmentUtility.Reason(f), f.loadID ^ 0x60F);
            Voice voice = FactionVoice.Of(f);
            Row(l, "Dominion_Dossier_Voice".Translate(), FactionVoice.Label(voice).CapitalizeFirst());
            MemeDef voiceMeme = FactionVoice.VoiceMeme(f);
            TooltipHandler.TipRegion(new Rect(0f, l.CurHeight - Line, l.ColumnWidth, Line), () => "Dominion_Dossier_Voice_Desc".Translate(f.Name, FactionVoice.Label(voice),
                voiceMeme != null ? "Dominion_Dossier_Voice_Meme".Translate(voiceMeme.LabelCap).ToString() : "") + "\n\n“" + FactionVoice.Line(f, VoiceLine.Greeting) + "”", f.loadID ^ 0x70C);
            Row(l, "Dominion_Col_Stability".Translate(), f, IntelUtility.RestrictedIntel, () => DominionUI.Colored(s.stability.ToString("F0"), DominionUI.StabilityColor(s.stability)));
            Row(l, "Dominion_Dossier_Legitimacy".Translate(), f, IntelUtility.RestrictedIntel, () => s.legitimacy.ToString("F0"));
            Row(l, "Dominion_Dossier_WarWeariness".Translate(), f, IntelUtility.SecretIntel, () => s.warWeariness.ToString("F0"));
            Row(l, "Dominion_Dossier_ActiveWars".Translate(), f, IntelUtility.RestrictedIntel, () => s.activeWars.ToString());
            Row(l, "Dominion_Dossier_Population".Translate(), f, IntelUtility.RestrictedIntel, () => s.population + " / " + s.totalLiving);
            Row(l, "Dominion_Dossier_Wealth".Translate(), f, IntelUtility.SecretIntel, () => s.wealth.ToStringMoney());
            Row(l, "Dominion_Col_Economy".Translate(), f, IntelUtility.RestrictedIntel, () => s.economyIndex.ToString("F0") + "  (" + s.climate.ToStringPercentSigned() + ")");
            if (IntelUtility.Knows(f, IntelUtility.RestrictedIntel))
                TooltipHandler.TipRegion(new Rect(0f, l.CurHeight - Line, l.ColumnWidth, Line), () => ClimateTip(f, s), f.loadID ^ 0x5C1);
            Row(l, "Dominion_Col_Military".Translate(), f, IntelUtility.RestrictedIntel, () => s.militaryStrength.ToString("F0"));
            Row(l, "Dominion_Dossier_Territory".Translate(), s.territoryTiles + " / " + s.settlementCount);
            Row(l, "Dominion_Dossier_VassalOutposts".Translate(), s.vassalOutposts.ToString());
            profileHeight = l.CurHeight + 4f;
            l.End();
            Widgets.EndScrollView();
        }

        /// <summary>A row whose value is only shown with at least <paramref name="needed"/> intel on the faction.</summary>
        private static void Row(Listing_Standard l, string label, Faction f, float needed, System.Func<string> value) =>
            Row(l, label, IntelUtility.Knows(f, needed) ? value() : IntelUtility.Unknown(needed));

        private static void Row(Listing_Standard l, string label, string value)
        {
            Rect r = l.GetRect(Line);
            GUI.color = Color.gray;
            Widgets.Label(r.LeftPart(0.48f), label);
            GUI.color = Color.white;
            Widgets.Label(r.RightPart(0.52f), value);
        }

        private static string ClimateTip(Faction f, FactionState s)
        {
            var sb = new System.Text.StringBuilder("Dominion_Climate_Header".Translate(s.climate.ToStringPercentSigned()).ToString());
            sb.AppendLine();
            EconomyEffects.ComputeClimate(f, s, WorldComponent_Dominion.Instance, sb);
            return sb.ToString();
        }

        private static void Locked(Rect rect, string title, float needed)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, Line), title);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y + Line + 4f, rect.width, Line * 3f), "Dominion_Dossier_Locked".Translate(needed.ToString("F0")));
            GUI.color = Color.white;
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

            // Rebuilt twice a second rather than every frame.
            float realNow = Time.realtimeSinceStartup;
            if (rowsFaction != f || realNow >= rowsNextTime)
            {
                rowsFaction = f;
                rowsNextTime = realNow + 0.5f;
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
            }

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
