using Verse;

namespace Dominion
{
    /// <summary>
    /// Vanilla Ideology Expanded - Memes and Structures. Nothing to bind: Dominion reads its memes and precepts by name.
    /// They decide governments (<see cref="GovernmentUtility.VanillaMemesGovernment"/>), temper the AI
    /// (<see cref="AIPersonality"/>), make some structures count as faiths, and give factions their voice in letters,
    /// audiences and news (<see cref="FactionVoice"/>).
    /// </summary>
    public class Integration_VanillaMemes : DominionIntegration
    {
        public override string PackageId => "VanillaExpanded.VMemesE";
        public override string Label => "Vanilla Ideology Expanded - Memes and Structures";

        protected internal override void Bind(Binder b) { }
    }
}
