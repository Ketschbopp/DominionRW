using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// How a faction's AI behaves. The base values are rolled once per faction; the effective values add
    /// the current ideology memes and the current leader's traits, so a new leader changes the faction.
    /// All values are 0..1.
    /// </summary>
    public class AIPersonality : IExposable
    {
        public float baseAggression = 0.5f;
        public float baseGreed = 0.5f;
        public float baseHonour = 0.5f;
        public float baseParanoia = 0.5f;

        [Unsaved] public float aggression, greed, honour, paranoia;

        private static readonly Dictionary<string, Vector4> MemeMods = new Dictionary<string, Vector4>
        {
            // x aggression, y greed, z honour, w paranoia
            { "Supremacist", new Vector4(0.25f, 0.05f, -0.05f, 0.10f) },
            { "Raider", new Vector4(0.30f, 0.15f, -0.20f, 0f) },
            { "Collectivist", new Vector4(0f, -0.05f, 0.10f, 0.05f) },
            { "Individualist", new Vector4(0f, 0.10f, -0.05f, 0f) },
            { "Loyalist", new Vector4(0.05f, 0f, 0.20f, 0.05f) },
            { "Proselytizer", new Vector4(0.10f, 0f, 0.05f, 0f) },
            { "HighLife", new Vector4(-0.05f, 0.15f, -0.05f, -0.05f) },
            { "PainIsVirtue", new Vector4(0.10f, 0f, 0.05f, 0f) },
            { "Cannibal", new Vector4(0.10f, 0f, -0.10f, 0f) },
            { "HumanPrimacy", new Vector4(0.05f, 0.05f, 0f, 0.05f) },
            { "NaturePrimacy", new Vector4(-0.10f, -0.10f, 0.05f, 0f) },
            { "TreeConnection", new Vector4(-0.10f, -0.10f, 0.05f, 0f) },
            { "Guilty", new Vector4(-0.10f, -0.05f, 0.10f, 0.05f) },
            { "Transhumanist", new Vector4(0f, 0.05f, 0f, -0.05f) },
            { "FleshPurity", new Vector4(0.05f, 0f, 0f, 0.10f) },

            // Vanilla Memes Expanded
            { "VME_Aristocratic", new Vector4(0f, 0.10f, 0.10f, 0.05f) },
            { "VME_Royal", new Vector4(0f, 0.05f, 0.15f, 0f) },
            { "VME_GodEmperor", new Vector4(0.10f, 0f, 0.10f, 0.10f) },
            { "VME_ExaltedPriesthood", new Vector4(0.05f, 0f, 0.10f, 0.05f) },
            { "VME_Republic", new Vector4(-0.05f, 0f, 0.10f, 0f) },
            { "VME_Gestalt", new Vector4(-0.05f, -0.05f, 0.10f, -0.05f) },
            { "VME_Egalitarian", new Vector4(-0.10f, -0.05f, 0.15f, -0.05f) },
            { "VME_Elders", new Vector4(-0.05f, 0f, 0.10f, 0.05f) },
            { "VME_Nationalist", new Vector4(0.15f, 0f, 0.05f, 0.15f) },
            { "VME_Structure_Authoritarianism", new Vector4(0.10f, 0f, 0f, 0.15f) },
            { "VME_Structure_CultOfPersonality", new Vector4(0.05f, 0.05f, 0f, 0.10f) },
            { "VME_Structure_Corporate", new Vector4(0f, 0.25f, -0.05f, 0f) },
            { "VME_Trader", new Vector4(-0.10f, 0.20f, 0.05f, -0.05f) },
            { "VME_ViolentConversion", new Vector4(0.20f, 0f, -0.05f, 0f) },
            { "VME_BloodCourt", new Vector4(0.25f, 0f, 0.10f, 0f) },
            { "VME_Bushido", new Vector4(0.10f, -0.05f, 0.25f, 0f) },
            { "VME_Pacifist", new Vector4(-0.30f, 0f, 0.15f, 0.05f) },
            { "VME_Emancipation", new Vector4(0f, -0.05f, 0.15f, 0f) },
            { "VFEA_Isolationist", new Vector4(0.10f, -0.05f, 0f, 0.30f) },
            { "VME_Anonymity", new Vector4(0f, 0f, 0f, 0.15f) },
            { "VME_Nomad", new Vector4(0.05f, 0f, 0f, 0.05f) },
            { "VME_CityBuilders", new Vector4(-0.05f, 0.10f, 0f, 0f) },
            { "VME_HardcoreIndustrialism", new Vector4(0f, 0.15f, 0f, 0f) },
            { "VME_Progressive", new Vector4(0f, 0.05f, 0f, -0.05f) },
            { "VME_CraftCulture", new Vector4(-0.05f, 0.05f, 0.10f, 0f) },
            { "VME_Scrapper", new Vector4(0f, 0.10f, 0f, 0f) },
            { "VME_PartyLife", new Vector4(-0.10f, 0.05f, -0.05f, -0.10f) },
            { "VME_Healthcare", new Vector4(-0.10f, 0f, 0.05f, 0f) },
            { "VME_Vegan", new Vector4(-0.05f, -0.05f, 0.05f, 0f) },
            { "VME_Light", new Vector4(0f, 0f, 0.05f, 0.05f) },
            { "VME_FireWorship", new Vector4(0.10f, 0f, 0f, 0f) },
            { "VME_HolyDiseases", new Vector4(0.05f, 0f, -0.05f, 0f) },
            { "VME_Fleshcrafters", new Vector4(0.05f, 0f, -0.10f, 0f) },
            { "VME_MechanoidSupremacy", new Vector4(0.10f, 0f, -0.10f, 0.05f) },
            { "VME_InsectoidSupremacy", new Vector4(0.15f, 0f, -0.10f, 0f) },
            { "VME_Astrology", new Vector4(0f, 0f, 0f, 0.05f) },
            { "VME_PsychicFocus", new Vector4(0f, 0f, 0.05f, 0.05f) },
            { "VME_Cultured", new Vector4(-0.05f, 0f, 0.05f, 0f) },
            { "VME_Structure_Serketist", new Vector4(0.10f, 0f, 0f, 0.05f) },
            { "VME_Structure_Esotericism", new Vector4(0f, 0f, -0.05f, 0.15f) },
            { "VME_Structure_ChthonianCult", new Vector4(0.10f, 0f, -0.10f, 0.10f) },
            { "VME_Structure_Eschatologism", new Vector4(0.15f, -0.05f, 0f, 0f) },
            { "VME_Structure_Shintaoism", new Vector4(-0.05f, 0f, 0.10f, 0f) },
            { "VME_Structure_Pantheism", new Vector4(-0.05f, 0f, 0.05f, 0f) },
            { "VME_Structure_Omnism", new Vector4(-0.10f, 0f, 0.05f, -0.10f) },
            { "VME_Structure_Bacchanalianism", new Vector4(-0.05f, 0.10f, -0.10f, -0.05f) },
            { "VME_Structure_SecularSpirituality", new Vector4(-0.05f, 0f, 0.05f, 0f) },
        };

        /// <summary>Precepts (vanilla issues with Vanilla Memes Expanded options) that tip a faction's temper.</summary>
        private static readonly Dictionary<string, Vector4> PreceptMods = new Dictionary<string, Vector4>
        {
            { "VME_Violence_Honorable", new Vector4(0.10f, 0f, 0.05f, 0f) },
            { "VME_Violence_Disapproved", new Vector4(-0.05f, 0f, 0f, 0f) },
            { "VME_Violence_Abhorrent", new Vector4(-0.15f, 0f, 0.05f, 0f) },
            { "VME_Raiding_Honorable", new Vector4(0.10f, 0.05f, -0.05f, 0f) },
            { "VME_Raiding_Abhorrent", new Vector4(-0.10f, 0f, 0.05f, 0f) },
            { "VME_Defeat_Dishonorable", new Vector4(0.05f, 0f, 0.05f, 0f) },
            { "VME_Trading_Required", new Vector4(0f, 0.10f, 0f, 0f) },
            { "VME_Slavery_Forbidden", new Vector4(0f, 0f, 0.10f, 0f) },
            { "VFEA_Recruiting_Forbidden", new Vector4(0f, 0f, 0f, 0.10f) },
            { "VME_Corruption_Essential", new Vector4(0f, 0.10f, -0.10f, 0.05f) },
        };

        private static readonly Dictionary<string, Vector4> TraitMods = new Dictionary<string, Vector4>
        {
            { "Bloodlust", new Vector4(0.20f, 0f, -0.05f, 0f) },
            { "Psychopath", new Vector4(0.15f, 0.10f, -0.25f, 0f) },
            { "Kind", new Vector4(-0.20f, -0.05f, 0.15f, -0.05f) },
            { "Greedy", new Vector4(0f, 0.20f, -0.05f, 0f) },
            { "Jealous", new Vector4(0.05f, 0.10f, 0f, 0.10f) },
            { "Abrasive", new Vector4(0.10f, 0f, 0f, 0.05f) },
            { "Ascetic", new Vector4(-0.05f, -0.15f, 0.05f, 0f) },
            { "Wimp", new Vector4(-0.15f, 0f, 0f, 0.15f) },
        };

        public void SeedFrom(Faction faction)
        {
            Rand.PushState(faction.loadID * 7919 + 17);
            try
            {
                baseAggression = Rand.Range(0.2f, 0.8f);
                baseGreed = Rand.Range(0.2f, 0.8f);
                baseHonour = Rand.Range(0.2f, 0.8f);
                baseParanoia = Rand.Range(0.2f, 0.8f);
            }
            finally
            {
                Rand.PopState();
            }
            if (faction.def.permanentEnemy)
            {
                baseAggression = Mathf.Max(baseAggression, 0.75f);
                baseHonour = Mathf.Min(baseHonour, 0.25f);
            }
            if (faction.def.techLevel <= TechLevel.Neolithic)
                baseParanoia = Mathf.Min(1f, baseParanoia + 0.1f);
        }

        /// <summary>Recomputes the effective values from base, memes and leader traits.</summary>
        public void Refresh(Faction faction)
        {
            Vector4 v = new Vector4(baseAggression, baseGreed, baseHonour, baseParanoia);

            Ideo ideo = faction.ideos?.PrimaryIdeo;
            if (ideo != null && ModsConfig.IdeologyActive)
                foreach (MemeDef meme in ideo.memes)
                    if (MemeMods.TryGetValue(meme.defName, out Vector4 m))
                        v += m;
            if (ideo != null && ModsConfig.IdeologyActive)
                foreach (Precept p in ideo.PreceptsListForReading)
                    if (PreceptMods.TryGetValue(p.def.defName, out Vector4 m))
                        v += m;

            List<Trait> traits = faction.leader?.story?.traits?.allTraits;
            if (traits != null)
                foreach (Trait t in traits)
                    if (TraitMods.TryGetValue(t.def.defName, out Vector4 m))
                        v += m;

            aggression = Mathf.Clamp01(v.x);
            greed = Mathf.Clamp01(v.y);
            honour = Mathf.Clamp01(v.z);
            paranoia = Mathf.Clamp01(v.w);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref baseAggression, "aggression", 0.5f);
            Scribe_Values.Look(ref baseGreed, "greed", 0.5f);
            Scribe_Values.Look(ref baseHonour, "honour", 0.5f);
            Scribe_Values.Look(ref baseParanoia, "paranoia", 0.5f);
        }
    }
}
