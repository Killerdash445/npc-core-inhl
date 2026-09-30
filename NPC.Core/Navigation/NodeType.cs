namespace NPC.Core.Navigation
{
    /// <summary>
    /// What a graph node stands on. A Stair node may be entered off-level, from its foot or head:
    /// docs/invariants.md#entry-seeds-on-own-deck. An Outdoor node lies outside an airlock, under
    /// open sky: routing enters one only for an NPC whose settings allow it (MayGoOutside),
    /// docs/navigation.md#node-types
    /// </summary>
    public enum NodeType
    {
        Ground = 0,
        Stair = 1,
        Outdoor = 2
    }
}
