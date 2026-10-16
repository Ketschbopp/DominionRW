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

        public static bool IsReligious(Faction f) =>
            HasMemeStartingWith(f, "Structure_Theist") || HasMemeStartingWith(f, "Structure_Origin");

        /// <summary>The government a faction starts with, from tech level, royal titles and ideology.</summary>
        public static GovernmentType Determine(Faction f)
        {
            TechLevel tech = f.def.techLevel;
            if (tech <= TechLevel.Neolithic) return GovernmentType.Tribal;
            if (f.def.HasRoyalTitles) return GovernmentType.Monarchy;
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
