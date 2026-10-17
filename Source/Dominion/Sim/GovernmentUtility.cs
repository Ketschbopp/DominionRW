using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    public static class GovernmentUtility
    {
        public static bool HasMeme(Faction f, string defName)
        {
            if (!ModsConfig.IdeologyActive) return false;
            List<MemeDef> memes = f.ideos?.PrimaryIdeo?.memes;
            if (memes == null) return false;
            for (int i = 0; i < memes.Count; i++)
                if (memes[i].defName == defName) return true;
            return false;
        }

        public static bool HasMemeStartingWith(Faction f, string prefix)
        {
            if (!ModsConfig.IdeologyActive) return false;
            List<MemeDef> memes = f.ideos?.PrimaryIdeo?.memes;
            if (memes == null) return false;
            for (int i = 0; i < memes.Count; i++)
                if (memes[i].defName.StartsWith(prefix)) return true;
            return false;
        }

        /// <summary>Vanilla Memes Expanded structures and memes that make a faith of the ideology.</summary>
        private static readonly string[] ReligiousMemes =
        {
            "VME_Structure_Serketist", "VME_Structure_Esotericism", "VME_Structure_ChthonianCult", "VME_Structure_Eschatologism",
            "VME_Structure_Shintaoism", "VME_Structure_Pantheism", "VME_Structure_Omnism", "VME_ViolentConversion", "VME_ExaltedPriesthood",
            "VME_GodEmperor",
        };

        /// <summary>Structures whose faith is strong enough to put priests in charge.</summary>
        private static readonly string[] TheocraticMemes =
        {
            "VME_Structure_Serketist", "VME_Structure_Esotericism", "VME_Structure_ChthonianCult", "VME_Structure_Eschatologism",
            "VME_ViolentConversion", "VME_ExaltedPriesthood",
        };

        public static bool IsReligious(Faction f) =>
            HasMemeStartingWith(f, "Structure_Theist") || HasMemeStartingWith(f, "Structure_Origin") || HasAnyMeme(f, ReligiousMemes);

        public static bool HasAnyMeme(Faction f, string[] defNames)
        {
            foreach (string d in defNames)
                if (HasMeme(f, d)) return true;
            return false;
        }

        /// <summary>
        /// The government a Vanilla Memes Expanded ideology demands, if any, and the meme that decides it. Checked in
        /// order of how strongly a meme says who rules: a blood court is a junta, kings and queens are a monarchy,
        /// priests a theocracy, strongmen an autocracy, councils and elders a council, senates a republic, companies a
        /// corporation.
        /// </summary>
        public static GovernmentType? VanillaMemesGovernment(Faction f, out string meme)
        {
            meme = null;
            if (!ModsConfig.IdeologyActive) return null;
            TechLevel tech = f.def.techLevel;
            (string meme, GovernmentType? gov)[] rules =
            {
                ("VME_BloodCourt", GovernmentType.Junta),
                ("VME_Aristocratic", GovernmentType.Monarchy),
                ("VME_Royal", GovernmentType.Monarchy),
                ("VME_Bushido", tech <= TechLevel.Medieval ? GovernmentType.Monarchy : (GovernmentType?)null),
                ("VME_GodEmperor", IsReligious(f) ? GovernmentType.Theocracy : GovernmentType.Autocracy),
                ("VME_ExaltedPriesthood", GovernmentType.Theocracy),
                ("VME_ViolentConversion", GovernmentType.Theocracy),
                ("VME_Structure_Serketist", GovernmentType.Theocracy),
                ("VME_Structure_Esotericism", GovernmentType.Theocracy),
                ("VME_Structure_ChthonianCult", GovernmentType.Theocracy),
                ("VME_Structure_Eschatologism", GovernmentType.Theocracy),
                ("VME_Nationalist", GovernmentType.Autocracy),
                ("VME_Structure_Authoritarianism", GovernmentType.Autocracy),
                ("VME_Structure_CultOfPersonality", GovernmentType.Autocracy),
                ("VFEA_Isolationist", GovernmentType.Autocracy),
                ("VME_Gestalt", GovernmentType.Council),
                ("VME_Egalitarian", GovernmentType.Council),
                ("VME_Elders", GovernmentType.Council),
                ("VME_Nomad", tech >= TechLevel.Medieval ? GovernmentType.Council : (GovernmentType?)null),
                ("VME_Republic", tech >= TechLevel.Spacer ? GovernmentType.Corporate : GovernmentType.Republic),
                ("VME_Structure_Corporate", tech >= TechLevel.Industrial ? GovernmentType.Corporate : GovernmentType.Republic),
                ("VME_Trader", tech >= TechLevel.Spacer ? GovernmentType.Corporate : GovernmentType.Republic),
            };
            foreach ((string m, GovernmentType? gov) in rules)
                if (gov.HasValue && HasMeme(f, m))
                {
                    meme = m;
                    return gov;
                }
            return null;
        }

        /// <summary>The government a faction starts with, from tech level, royal titles and ideology.</summary>
        public static GovernmentType Determine(Faction f)
        {
            TechLevel tech = f.def.techLevel;
            if (tech <= TechLevel.Neolithic) return GovernmentType.Tribal;
            if (f.def.HasRoyalTitles) return GovernmentType.Monarchy;
            if (VanillaMemesGovernment(f, out _) is GovernmentType vme && !(f.def.permanentEnemy && vme != GovernmentType.Junta && vme != GovernmentType.Autocracy))
                return vme;
            if (f.def.permanentEnemy || HasMeme(f, "Raider") || HasMeme(f, "Supremacist")) return GovernmentType.Junta;
            if (HasMeme(f, "Loyalist")) return GovernmentType.Autocracy;
            if (IsReligious(f) && (HasMeme(f, "Proselytizer") || tech <= TechLevel.Medieval)) return GovernmentType.Theocracy;
            if (HasMeme(f, "Collectivist")) return GovernmentType.Council;
            if (HasMeme(f, "Individualist") || HasMeme(f, "HighLife"))
                return tech >= TechLevel.Spacer ? GovernmentType.Corporate : GovernmentType.Republic;
            if (tech <= TechLevel.Medieval) return GovernmentType.Autocracy;
            return tech >= TechLevel.Spacer ? GovernmentType.Corporate : GovernmentType.Republic;
        }

        /// <summary>What a coup installs: the military takes over, except in tribes and monarchies (palace coup).</summary>
        public static GovernmentType AfterCoup(GovernmentType g)
        {
            switch (g)
            {
                case GovernmentType.Tribal: return GovernmentType.Tribal;
                case GovernmentType.Monarchy: return GovernmentType.Monarchy;
                default: return GovernmentType.Junta;
            }
        }

        /// <summary>What a revolution installs: authoritarian regimes fall to popular ones and the reverse.</summary>
        public static GovernmentType AfterRevolution(GovernmentType g)
        {
            switch (g)
            {
                case GovernmentType.Tribal: return GovernmentType.Tribal;
                case GovernmentType.Republic:
                case GovernmentType.Council:
                case GovernmentType.Corporate:
                    return GovernmentType.Autocracy;
                default:
                    return Rand.Bool ? GovernmentType.Republic : GovernmentType.Council;
            }
        }

        /// <summary>Flat stability bonus a government type gives.</summary>
        public static float StabilityBonus(GovernmentType g)
        {
            switch (g)
            {
                case GovernmentType.Monarchy: return 8f;
                case GovernmentType.Autocracy:
                case GovernmentType.Theocracy:
                case GovernmentType.Tribal:
                    return 5f;
                case GovernmentType.Junta: return -5f;
                default: return 0f;
            }
        }

        public static float ZealotBase(Faction f) => IsReligious(f) || HasMeme(f, "Proselytizer") ? 0.25f : 0.05f;

        /// <summary>Why a faction has the government it has, in a few words, for tooltips: the meme or tradition behind it.</summary>
        public static string Reason(Faction f)
        {
            if (f.def.techLevel <= TechLevel.Neolithic) return "Dominion_GovReason_Tribal".Translate();
            if (f.def.HasRoyalTitles) return "Dominion_GovReason_Royal".Translate();
            if (VanillaMemesGovernment(f, out string meme) != null)
            {
                MemeDef def = DefDatabase<MemeDef>.GetNamedSilentFail(meme);
                return "Dominion_GovReason_Meme".Translate(def?.LabelCap ?? meme);
            }
            return "Dominion_GovReason_Tradition".Translate();
        }

        public static void InitializeBlocs(FactionState s)
        {
            AIPersonality p = s.personality;
            s.SetBlocs(
                0.15f + p.aggression * 0.25f,
                0.15f + (1f - p.aggression) * 0.2f,
                0.10f + p.greed * 0.2f,
                ZealotBase(s.faction),
                0.08f);
        }
    }
}
