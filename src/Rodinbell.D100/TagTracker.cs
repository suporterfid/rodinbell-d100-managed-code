namespace Rodinbell.D100;

internal sealed class TagTracker(TimeSpan window, int capacity)
{
    private sealed class Entry(TagRead tag, LinkedListNode<string> node)
    {
        public DateTimeOffset First = tag.Timestamp;
        public DateTimeOffset Emitted;
        public long Count;
        public LinkedListNode<string> Node = node;
    }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recent = new();
    internal int Count => entries.Count;

    public TagRead? Observe(TagRead tag)
    {
        string key = (tag.Epc ?? tag.IdentifierHex) + ":" + tag.Tid;
        if (!entries.TryGetValue(key, out var entry))
        {
            if (entries.Count >= capacity)
            {
                string oldest = recent.First!.Value;
                recent.RemoveFirst(); entries.Remove(oldest);
            }
            entry = new Entry(tag, recent.AddLast(key)); entries.Add(key, entry);
        }
        else { recent.Remove(entry.Node); recent.AddLast(entry.Node); }
        entry.Count += tag.ReadCount;
        if (entry.Emitted != default && tag.Timestamp - entry.Emitted < window) return null;
        entry.Emitted = tag.Timestamp;
        return tag with { FirstSeen = entry.First, LastSeen = tag.Timestamp, ReadCount = entry.Count };
    }
}
