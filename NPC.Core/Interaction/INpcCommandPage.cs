namespace NPC.Core.Interaction
{
    /// <summary>
    /// Optional command-page navigation. Existing conversations still return to the message log.
    /// Read after Answer; true refreshes Commands in place. docs/interaction.md#2-the-panel
    /// </summary>
    public interface INpcCommandPage
    {
        bool KeepCommandsOpen { get; }
    }
}
