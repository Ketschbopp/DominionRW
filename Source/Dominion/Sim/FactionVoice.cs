using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>How a faction speaks: the manner of its letters, replies and audiences.</summary>
    public enum Voice { Plain, Courtly, Divine, Zealous, Eerie, Martial, Honourable, Stern, Aloof, Mercantile, Festive, Gentle, Collegial, Tribal }

    /// <summary>The moments a faction says something to you.</summary>
    public enum VoiceLine { Greeting, Accept, Refuse, Thanks, Insulted, Threat, Farewell }

    /// <summary>
    /// A faction's voice, from its ideology's memes (vanilla and Vanilla Memes Expanded), its government and its temper.
    /// Lines come from keys "Dominion_Voice_{voice}_{line}", each holding variants separated by "|", with {0} the
    /// faction's name, {1} the leader's and {2} the colony's; a missing key falls back to the plain voice.
    /// </summary>
    public static class FactionVoice
    {
        private static readonly (Voice voice, string[] memes)[] ByMeme =
        {
            (Voice.Divine, new[] { "VME_GodEmperor", "VME_ExaltedPriesthood", "VME_Structure_CultOfPersonality" }),
            (Voice.Eerie, new[] { "VME_Structure_ChthonianCult", "VME_Structure_Esotericism", "VME_Structure_Serketist", "VME_Fleshcrafters", "VME_HolyDiseases",
                "VME_MechanoidSupremacy", "VME_InsectoidSupremacy", "Cannibal" }),
            (Voice.Zealous, new[] { "VME_ViolentConversion", "VME_FireWorship", "VME_Structure_Eschatologism", "Proselytizer" }),
            (Voice.Martial, new[] { "VME_BloodCourt", "Raider", "Supremacist", "PainIsVirtue" }),
            (Voice.Honourable, new[] { "VME_Bushido" }),
            (Voice.Courtly, new[] { "VME_Aristocratic", "VME_Royal" }),
            (Voice.Stern, new[] { "VME_Nationalist", "VME_Structure_Authoritarianism", "Loyalist" }),
            (Voice.Aloof, new[] { "VFEA_Isolationist", "VME_Anonymity", "VME_Astrology" }),
            (Voice.Mercantile, new[] { "VME_Trader", "VME_Structure_Corporate", "VME_HardcoreIndustrialism", "VME_Scrapper" }),
            (Voice.Festive, new[] { "VME_PartyLife", "VME_Structure_Bacchanalianism", "HighLife" }),
            (Voice.Gentle, new[] { "VME_Pacifist", "VME_Healthcare", "VME_Vegan", "VME_Egalitarian", "NaturePrimacy", "TreeConnection", "Guilty" }),
            (Voice.Collegial, new[] { "VME_Republic", "VME_Gestalt", "VME_Elders", "Collectivist" }),
        };

        private static readonly UICache<Faction, Voice> cache = new UICache<Faction, Voice>(Determine, 30f);

        public static Voice Of(Faction f) => f == null ? Voice.Plain : cache.Get(f);

        /// <summary>The meme that gives the faction its voice, if one does.</summary>
        public static MemeDef VoiceMeme(Faction f)
        {
            if (f == null) return null;
            foreach ((Voice _, string[] memes) in ByMeme)
                foreach (string m in memes)
                    if (GovernmentUtility.HasMeme(f, m)) return DefDatabase<MemeDef>.GetNamedSilentFail(m);
            return null;
        }

        private static Voice Determine(Faction f)
        {
            foreach ((Voice voice, string[] memes) in ByMeme)
                if (GovernmentUtility.HasAnyMeme(f, memes)) return voice;
            if (f.def.HasRoyalTitles) return Voice.Courtly;
            if (f.def.techLevel <= TechLevel.Neolithic) return Voice.Tribal;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp != null && comp.TryGetState(f, out FactionState s) && s.governmentInitialized)
            {
                switch (s.government)
                {
                    case GovernmentType.Tribal: return Voice.Tribal;
                    case GovernmentType.Monarchy: return Voice.Courtly;
                    case GovernmentType.Theocracy: return Voice.Divine;
                    case GovernmentType.Junta: return Voice.Martial;
                    case GovernmentType.Autocracy: return Voice.Stern;
                    case GovernmentType.Corporate: return Voice.Mercantile;
                    case GovernmentType.Council:
                    case GovernmentType.Republic: return Voice.Collegial;
                }
                if (s.personality.aggression > 0.75f) return Voice.Stern;
            }
            return Voice.Plain;
        }

        public static string Label(Voice v) => ("Dominion_Voice_" + v).Translate();

        /// <summary>
        /// A line in the faction's voice. <paramref name="seed"/> picks among the variants, so the same moment reads the
        /// same if drawn again; pass something that changes when the moment does (an event id, a tick).
        /// </summary>
        public static string Line(Faction f, VoiceLine line, int seed = 0)
        {
            Voice v = Of(f);
            string key = "Dominion_Voice_" + v + "_" + line;
            if (!key.CanTranslate()) key = "Dominion_Voice_Plain_" + line;
            string leader = f?.leader?.LabelShortCap ?? f?.Name ?? "?";
            string colony = Faction.OfPlayerSilentFail?.Name ?? "?";
            string[] variants = key.Translate(f?.Name ?? "?", leader, colony).ToString().Split('|');
            int i = variants.Length <= 1 ? 0 : (int)((uint)(seed * 31 + (f?.loadID ?? 0) * 17 + (int)line) % (uint)variants.Length);
            return variants[i].Trim();
        }

        /// <summary>The line a faction answers an action with, from how it went (1 well, -1 badly, 0 neither); hostile acts are met with offence.</summary>
        public static string Answer(Faction f, int outcome, bool hostileAction, int seed) =>
            Line(f, hostileAction ? VoiceLine.Insulted : outcome > 0 ? VoiceLine.Accept : outcome < 0 ? VoiceLine.Refuse : VoiceLine.Greeting, seed);
    }
}
