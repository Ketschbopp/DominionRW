using System.Collections.Generic;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The player's news: items on their way (pending), events the player cannot see yet (hidden), and
    /// delivered reports (capped history, oldest dropped first).
    /// </summary>
    public class NewsLog : IExposable
    {
        private List<NewsItem> delivered = new List<NewsItem>();
        private List<NewsItem> pending = new List<NewsItem>();
        private int nextId;

        public IReadOnlyList<NewsItem> Delivered => delivered;
        public List<NewsItem> Pending => pending;

        public int UnreadCount { get; private set; }

        public NewsItem Add(NewsItem item)
        {
            item.id = nextId++;
            pending.Add(item);
            return item;
        }

        public void MarkDelivered(NewsItem item)
        {
            pending.Remove(item);
            item.delivered = true;
            delivered.Add(item);
            if (!item.read) UnreadCount++;
            int cap = DominionMod.Settings?.newsLogCap ?? 300;
            int overflow = delivered.Count - cap;
            if (overflow > 0)
            {
                for (int i = 0; i < overflow; i++)
                    if (!delivered[i].read) UnreadCount--;
                delivered.RemoveRange(0, overflow);
            }
        }

        public void MarkRead(NewsItem item)
        {
            if (item.read) return;
            item.read = true;
            UnreadCount--;
        }

        public void MarkAllRead()
        {
            foreach (NewsItem n in delivered)
                n.read = true;
            UnreadCount = 0;
        }

        public NewsItem FindDelivered(int id)
        {
            for (int i = delivered.Count - 1; i >= 0; i--)
                if (delivered[i].id == id) return delivered[i];
            return null;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextId, "nextId");
            Scribe_Collections.Look(ref delivered, "delivered", LookMode.Deep);
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                delivered ??= new List<NewsItem>();
                pending ??= new List<NewsItem>();
                delivered.RemoveAll(n => n?.def == null);
                pending.RemoveAll(n => n?.def == null);
                UnreadCount = 0;
                foreach (NewsItem n in delivered)
                    if (!n.read) UnreadCount++;
            }
        }
    }
}
