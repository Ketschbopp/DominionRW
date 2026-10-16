namespace Dominion
{
    /// <summary>Gives every active integration its daily hook and the event stream.</summary>
    public class IntegrationSystem : SimSystem
    {
        public override int Order => 5;

        public override void DailyGlobal() => IntegrationRegistry.ForEachActive(i => i.DailyGlobal());

        public override void OnEvent(DominionEvent ev) => IntegrationRegistry.ForEachActive(i => i.OnEvent(ev));
    }
}
