namespace NPC.Core.Agents
{
    /// <summary>
    /// What an agent may do. Read live, so an override can return a mod's config entry.
    /// </summary>
    public class NpcAgentSettings
    {
        /// <summary>
        /// The agent's speed when it is attached; <see cref="NpcAgent.MoveSpeed"/> after that.
        /// </summary>
        public virtual float MoveSpeed => 3.5f;

        /// <summary>
        /// Opens the room doors in its way and closes them behind itself: docs/doors.md
        /// </summary>
        public virtual bool CanOpenDoors => true;

        /// <summary>
        /// Teleported back inside when it ends up in open space.
        /// </summary>
        public virtual bool KeepOutOfSpace => true;

        /// <summary>
        /// Can die at all. Off, neither the air nor the monster kills it.
        /// </summary>
        public virtual bool Mortal => true;

        /// <summary>
        /// The Breathless catches it: docs/invariants.md#one-catch-at-a-time
        /// </summary>
        public virtual bool CanBeCaught => true;

        /// <summary>
        /// Bad air kills it by the player's rule: docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        public virtual bool BreathesAir => true;

        /// <summary>
        /// Added to the room's temperature, as a suit does: 100 is the player's PilotSuit, +1 C.
        /// </summary>
        public virtual int SuitTemperatureResistance => 100;

        /// <summary>
        /// Wears an isolated suit, as the player's Space_Suit does: flat 2200/2200 whatever the room,
        /// so no atmosphere band ever applies, space included. docs/game-model.md#atmosphere-kills-by-the-players-rule
        /// </summary>
        public virtual bool Suited => false;

        /// <summary>
        /// May route through nodes marked <see cref="NPC.Core.Navigation.NodeType.Outdoor"/> - walk out
        /// of an airlock onto a surface. A suit makes this safe; off, the graph keeps the NPC inside.
        /// docs/navigation.md#node-types
        /// </summary>
        public virtual bool MayGoOutside => false;

        /// <summary>
        /// Multiplies <see cref="NpcAgent.MoveSpeed"/> while it walks: a worn suit's
        /// MovementSpeedModifier, as PlayerController.MovementSpeed applies it (Space_Suit 0.4).
        /// docs/agent.md#1-attaching-an-agent
        /// </summary>
        public virtual float SpeedFactor => 1f;

        /// <summary>
        /// Hops low obstacles in its way. Off, as for a player in an isolated suit
        /// (PlayerController.Jump): docs/agent.md#1-attaching-an-agent
        /// </summary>
        public virtual bool CanJump => true;

        /// <summary>
        /// Its ragdoll can be picked up once dead: <see cref="World.NpcCorpse"/>.
        /// </summary>
        public virtual bool CarryableCorpse => true;

        /// <summary>
        /// Drawn on the ship's lifecare terminal like the player: docs/lifecare.md
        /// </summary>
        public virtual bool ShowOnLifecare => true;

        /// <summary>
        /// Draws its target, plan and probes in the world, and allows the level-3 obstacle report.
        /// </summary>
        public virtual bool DebugVisuals => false;
    }
}
